using UnityEngine;

namespace SquishLab
{
    /// <summary>
    /// 1本の指とスクイーズ表面の接触。座標はすべてスクイーズのローカル（静止形状）空間。
    /// 静止形状に対して Raycast するので、変形中でも接触点が暴れない。
    /// </summary>
    public struct SquishContact
    {
        public int FingerId;

        /// <summary>現在の接触点（静止形状上）。</summary>
        public Vector3 Point;

        /// <summary>接触点の静止形状の法線（外向き）。</summary>
        public Vector3 Normal;

        /// <summary>
        /// 指が「掴んでいる」素材の元の位置。Point との差が表面の横方向の引っ張りになる。
        /// 伸びの限界を超えると Anchor が指側へ滑る（摩擦スリップ）。
        /// </summary>
        public Vector3 Anchor;

        /// <summary>押し始めてからの経過秒。押し続けるほどじわっと深く沈む。</summary>
        public float HeldTime;

        /// <summary>指の接触面の大きさによる半径倍率（iOS の touch.radius 由来, 通常 1）。</summary>
        public float RadiusMul;
    }
}
