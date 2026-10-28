// 电量显示的"抗反弹"规则：放电过程中，电量计偶尔会把读数往回弹几个点
// （电压回落、固件重新估算都会这样），显示上压住这种小反弹，免得用户以为在充电。
using System;

namespace RazerMouse.App
{
    internal static class BatterySmoother
    {
        /// <summary>小于等于这个幅度的回升当作噪声压掉（设备刻度 0–255，1 点 ≈ 0.4%）。</summary>
        public const int BounceTolerance = 5;

        /// <param name="lastPercent">上次显示的百分比，-1 表示还没有历史。</param>
        /// <param name="lastCharging">上次是否在充电。</param>
        /// <param name="newPercent">本次读到的百分比。</param>
        /// <param name="charging">本次是否在充电。</param>
        /// <returns>实际要显示的百分比。</returns>
        public static int Adjust(int lastPercent, bool lastCharging, int newPercent, bool charging)
        {
            if (lastPercent < 0) return newPercent;            // 第一次读，直接用
            if (charging || lastCharging) return newPercent;    // 插着电会往上走，正常
            if (newPercent <= lastPercent) return newPercent;   // 掉电，正常
            if (newPercent - lastPercent <= BounceTolerance) return lastPercent;  // 小幅回升：压住

            return newPercent;                                  // 大幅跳变：当真（换了电池/重新校准）
        }
    }
}
