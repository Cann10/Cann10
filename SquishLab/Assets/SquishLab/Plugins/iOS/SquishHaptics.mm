// SQUISH LAB — Core Haptics bridge.
//
// スクイーズの触感は「強い単発」ではなく「柔らかく細かい連続振動」が主役なので、
// CHHapticAdvancedPatternPlayer で連続イベントを1本流し続け、
// 押し込み量・押し込み速度に応じて強さ(intensity)と鋭さ(sharpness)を毎フレーム変える。
// 押し始め・限界付近・離した瞬間だけ、ごく弱い transient を重ねる。
//
// Core Haptics 非対応端末（iPhone 7 以前など）は UIImpactFeedbackGenerator(Soft) にフォールバック。

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <QuartzCore/QuartzCore.h>
#import <CoreHaptics/CoreHaptics.h>

static CHHapticEngine *sEngine API_AVAILABLE(ios(13.0)) = nil;
static id<CHHapticAdvancedPatternPlayer> sContinuous API_AVAILABLE(ios(13.0)) = nil;
static BOOL sEngineRunning = NO;
static BOOL sEngineFailed = NO;
static CFTimeInterval sContinuousStart = 0;
static UIImpactFeedbackGenerator *sImpact = nil;

// 連続イベントの最大長（Core Haptics の上限は 30 秒）。長押しでは張り直す。
static const NSTimeInterval kContinuousDuration = 30.0;

static float Clamp01(float v) { return v < 0.f ? 0.f : (v > 1.f ? 1.f : v); }

static BOOL SHSupported(void)
{
    if (@available(iOS 13.0, *))
        return CHHapticEngine.capabilitiesForHardware.supportsHaptics;
    return NO;
}

static BOOL SHEnsureEngine(void) API_AVAILABLE(ios(13.0))
{
    if (sEngineFailed) return NO;
    if (sEngine != nil && sEngineRunning) return YES;

    NSError *error = nil;
    if (sEngine == nil)
    {
        sEngine = [[CHHapticEngine alloc] initAndReturnError:&error];
        if (sEngine == nil)
        {
            NSLog(@"[SquishHaptics] engine create failed: %@", error);
            sEngineFailed = YES;
            return NO;
        }
        sEngine.playsHapticsOnly = YES;      // Unity の音声セッションに触らない
        sEngine.autoShutdownEnabled = YES;   // 触っていない間は省電力
        sEngine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
            dispatch_async(dispatch_get_main_queue(), ^{
                sEngineRunning = NO;
                sContinuous = nil;
            });
        };
        sEngine.resetHandler = ^{
            dispatch_async(dispatch_get_main_queue(), ^{
                sEngineRunning = NO;
                sContinuous = nil;
            });
        };
    }

    if (![sEngine startAndReturnError:&error])
    {
        NSLog(@"[SquishHaptics] engine start failed: %@", error);
        return NO;
    }
    sEngineRunning = YES;
    return YES;
}

static void SHImpactFallback(float intensity)
{
    if (sImpact == nil)
    {
        if (@available(iOS 13.0, *))
            sImpact = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
        else
            sImpact = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        [sImpact prepare];
    }
    if (@available(iOS 13.0, *))
        [sImpact impactOccurredWithIntensity:Clamp01(intensity)];
    else
        [sImpact impactOccurred];
    [sImpact prepare];
}

static void SHSendContinuous(float intensity, float sharpness) API_AVAILABLE(ios(13.0))
{
    if (sContinuous == nil) return;
    CHHapticDynamicParameter *i =
        [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                        value:Clamp01(intensity)
                                                 relativeTime:0];
    // ベースの sharpness は 0。Control は加算 (-1..1) なのでそのまま目標値になる。
    CHHapticDynamicParameter *s =
        [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticSharpnessControl
                                                        value:Clamp01(sharpness)
                                                 relativeTime:0];
    NSError *error = nil;
    if (![sContinuous sendParameters:@[ i, s ] atTime:CHHapticTimeImmediate error:&error])
        sContinuous = nil; // エンジンが止まった等。次の Update で張り直す
}

static void SHStartContinuous(float intensity, float sharpness) API_AVAILABLE(ios(13.0))
{
    if (!SHEnsureEngine()) return;
    if (sContinuous != nil)
    {
        [sContinuous stopAtTime:CHHapticTimeImmediate error:nil];
        sContinuous = nil;
    }

    NSError *error = nil;
    CHHapticEventParameter *pi =
        [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:1.0f];
    CHHapticEventParameter *ps =
        [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:0.0f];
    CHHapticEvent *ev = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                      parameters:@[ pi, ps ]
                                                    relativeTime:0
                                                        duration:kContinuousDuration];
    CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[ ev ] parameters:@[] error:&error];
    if (pattern == nil) return;

    id<CHHapticAdvancedPatternPlayer> player = [sEngine createAdvancedPlayerWithPattern:pattern error:&error];
    if (player == nil) return;

    sContinuous = player;
    // 開始前に強さを設定してから再生（最初の 1 フレームだけ全開で鳴るのを防ぐ）
    SHSendContinuous(intensity, sharpness);
    if (![player startAtTime:CHHapticTimeImmediate error:&error])
    {
        sContinuous = nil;
        return;
    }
    sContinuousStart = CACurrentMediaTime();
}

extern "C" {

bool SquishHaptics_IsSupported(void)
{
    return SHSupported();
}

void SquishHaptics_Prepare(void)
{
    if (@available(iOS 13.0, *))
    {
        if (SHSupported()) { SHEnsureEngine(); return; }
    }
    SHImpactFallback(0.0f);
}

void SquishHaptics_StartContinuous(float intensity, float sharpness)
{
    if (@available(iOS 13.0, *))
    {
        if (SHSupported()) SHStartContinuous(intensity, sharpness);
    }
}

void SquishHaptics_UpdateContinuous(float intensity, float sharpness)
{
    if (@available(iOS 13.0, *))
    {
        if (!SHSupported()) return;
        BOOL expired = sContinuous != nil && CACurrentMediaTime() - sContinuousStart > kContinuousDuration - 1.0;
        if (sContinuous == nil || expired)
            SHStartContinuous(intensity, sharpness);
        else
            SHSendContinuous(intensity, sharpness);
    }
}

void SquishHaptics_StopContinuous(void)
{
    if (@available(iOS 13.0, *))
    {
        if (sContinuous != nil)
        {
            [sContinuous stopAtTime:CHHapticTimeImmediate error:nil];
            sContinuous = nil;
        }
    }
}

void SquishHaptics_Transient(float intensity, float sharpness)
{
    if (@available(iOS 13.0, *))
    {
        if (SHSupported())
        {
            if (!SHEnsureEngine()) return;
            NSError *error = nil;
            CHHapticEventParameter *pi =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                              value:Clamp01(intensity)];
            CHHapticEventParameter *ps =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                              value:Clamp01(sharpness)];
            CHHapticEvent *ev = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient
                                                              parameters:@[ pi, ps ]
                                                            relativeTime:0];
            CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[ ev ] parameters:@[] error:&error];
            if (pattern == nil) return;
            id<CHHapticPatternPlayer> player = [sEngine createPlayerWithPattern:pattern error:&error];
            [player startAtTime:CHHapticTimeImmediate error:&error];
            return;
        }
    }
    SHImpactFallback(intensity);
}

} // extern "C"
