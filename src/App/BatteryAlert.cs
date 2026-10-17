// 低电量判定：带回差，避免在阈值附近来回跳导致反复弹提示。
using System;

namespace RazerMouse.App
{
    internal sealed class BatteryAlert
    {
        private const int RearmMargin = 10;   // 电量回升到 阈值+10 才重新武装

        private readonly int _threshold;
        private bool _armed = true;           // 低电量提示是否还有效
        private bool _chargedNotified;
        private bool _wasCharging;

        public BatteryAlert(int threshold)
        {
            _threshold = threshold < 1 ? 1 : (threshold > 90 ? 90 : threshold);
        }

        public int Threshold { get { return _threshold; } }

        /// <summary>标题、正文、是否算严重（严重时托盘图标会变红）。</summary>
        public event EventHandler<BatteryAlertEventArgs> Raised;

        public void Update(MouseStatus status)
        {
            if (!status.Connected || !status.HasBattery) return;

            int percent = status.BatteryPercent;

            if (status.Charging)
            {
                _armed = true;                 // 插上电就重新武装
                if (!_wasCharging) _chargedNotified = false;
                _wasCharging = true;

                if (percent >= 100 && !_chargedNotified)
                {
                    _chargedNotified = true;
                    Raise("电量已充满", "鼠标电池已充到 100%，可以拔线了。", false);
                }
                return;
            }

            if (_wasCharging)
            {
                _wasCharging = false;
                _chargedNotified = false;
            }

            if (percent > _threshold + RearmMargin) { _armed = true; return; }
            if (percent > _threshold) return;
            if (!_armed) return;

            _armed = false;
            bool critical = percent <= 5;
            string text = critical
                ? string.Format("电量只剩 {0}%，鼠标随时可能断连，请立刻充电。", percent)
                : string.Format("电量 {0}%，低于设定的 {1}%，记得充电。", percent, _threshold);
            Raise(critical ? "电量严重不足" : "鼠标电量偏低", text, critical);
        }

        private void Raise(string title, string text, bool critical)
        {
            EventHandler<BatteryAlertEventArgs> handler = Raised;
            if (handler == null) return;
            try { handler(this, new BatteryAlertEventArgs(title, text, critical)); } catch { }
        }
    }

    internal sealed class BatteryAlertEventArgs : EventArgs
    {
        public readonly string Title;
        public readonly string Text;
        public readonly bool Critical;

        public BatteryAlertEventArgs(string title, string text, bool critical)
        {
            Title = title;
            Text = text;
            Critical = critical;
        }
    }
}
