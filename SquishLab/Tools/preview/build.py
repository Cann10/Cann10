"""Unity なしで見た目を確認するプレビュー。

Assets の実シェーダー（SquishFoamCommon.hlsl / SquishFloorCommon.hlsl）を機械的に GLSL へ変換し、
テストが書き出した変形メッシュ（OBJ）を WebGL で描く。カメラ・照明は StudioStage と同じ計算。

  cd SquishLab/Tests && dotnet run -c Release -- --dump /tmp/squish
  python3 ../Tools/preview/build.py /tmp/squish rest pressed released1s
  node ../Tools/preview/shot.js rest pressed released1s     # → PNG
"""
import re, json, sys, os
HERE=os.path.dirname(os.path.abspath(__file__))
SH=os.path.join(HERE,'../../Assets/SquishLab/Resources/Shaders')+'/'
OBJ=sys.argv[1].rstrip('/')+'/'
sys.argv=[sys.argv[0]]+sys.argv[2:]
PRE='''#version 300 es
precision highp float;
#define float2 vec2
#define float3 vec3
#define float4 vec4
#define lerp mix
#define frac fract
#define ddx dFdx
#define ddy dFdy
float saturate(float x){return clamp(x,0.0,1.0);}
vec2 saturate(vec2 x){return clamp(x,0.0,1.0);}
vec3 saturate(vec3 x){return clamp(x,0.0,1.0);}
uniform vec3 uCam;
#define SQ_CAMERA_POS uCam
'''
def tr(src):
    src=re.sub(r'SqVaryings SqVert\(SqAttributes v\)\s*\{.*?\n\}\n','',src,flags=re.S)
    src=re.sub(r'struct SqAttributes\s*\{.*?\};','',src,flags=re.S)
    src=re.sub(r'\s*:\s*(SV_\w+|POSITION|NORMAL|COLOR|TEXCOORD\d)\b','',src)
    src=re.sub(r'^(float4|float)\s+(_\w+(?:\s*,\s*_\w+)*);',lambda m:'uniform '+{'float4':'vec4','float':'float'}[m.group(1)]+' '+m.group(2)+';',src,flags=re.M)
    return src
foam=tr(open(SH+'SquishFoamCommon.hlsl').read())
floor=tr(open(SH+'SquishFloorCommon.hlsl').read())
# shader Properties defaults
def props(path):
    d={}
    for m in re.finditer(r'(_\w+)\s*\("[^"]*",\s*(Color|Vector|Float|Range\([^)]*\))\)\s*=\s*\(?([^)\n]*)\)?',open(path).read()):
        name,typ,val=m.groups()
        vals=[float(x) for x in val.split(',')]
        d[name]=(typ,vals)
    return d
P=props(SH+'SquishFoam.shader'); F=props(SH+'SquishFloor.shader')
def lin(c): return [ (x**2.2) for x in c[:3]]+[1.0]
def uni(d):
    out={}
    for k,(t,v) in d.items():
        out[k]= lin(v) if t=='Color' else (v+[0,0,0,0])[:4] if t=='Vector' else v[0]
    return out
def load(n):
    V=[];C=[];T=[]
    for l in open(OBJ+n+'.obj'):
        a=l.split()
        if not a: continue
        if a[0]=='v': V+= [float(x) for x in a[1:4]]
        elif a[0]=='vc': C+= [float(x) for x in a[1:3]]
        elif a[0]=='f': T+= [int(x)-1 for x in a[1:4]]
    return V,C,T
frames={n:load(n) for n in sys.argv[1:]}
rest=frames[sys.argv[1]][0]
data={'rest':rest,'tris':frames[sys.argv[1]][2],'frames':{n:{'pos':v[0],'col':v[1]} for n,v in frames.items()},
      'foam':uni(P),'floor':uni(F)}
html=open(os.path.join(HERE,'template.html')).read().replace('/*FOAM*/',json.dumps(PRE+foam)).replace('/*FLOOR*/',json.dumps(PRE+floor)).replace('/*DATA*/',json.dumps(data))
open(os.path.join(HERE,'preview.html'),'w').write(html)
print('ok', len(rest)//3)
