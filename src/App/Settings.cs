// 本地配置：%APPDATA%\RazerMouse\config.ini（key=value，够用且不引入任何依赖）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RazerMouse.App
{
    internal sealed class Settings
    {
        public List<int> DpiStages;
        public int ActiveStage;
        public int PollingHz;
        public int LowBatteryThreshold;
        public bool StoreOnboard;
        public bool AutoStart;
        public bool RestoreOnExit;
        public int PanelX;
        public int PanelY;

        public Settings()
        {
            DpiStages = new List<int>(new int[] { 400, 800, 1600, 3200, 6400 });
            ActiveStage = 4;
            PollingHz = 1000;
            LowBatteryThreshold = 20;
            StoreOnboard = false;
            AutoStart = false;
            RestoreOnExit = false;
            PanelX = -1;
            PanelY = -1;
        }

        public static string FolderPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RazerMouse");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "config.ini"); }
        }

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return settings;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();

                    switch (key)
                    {
                        case "dpi_stages":
                            List<int> stages = ParseStages(value);
                            if (stages.Count > 0) settings.DpiStages = stages;
                            break;
                        case "active_stage":
                            settings.ActiveStage = ParseInt(value, settings.ActiveStage);
                            break;
                        case "polling_hz":
                            settings.PollingHz = ParseInt(value, settings.PollingHz);
                            break;
                        case "low_battery":
                            settings.LowBatteryThreshold = ParseInt(value, settings.LowBatteryThreshold);
                            break;
                        case "store_onboard":
                            settings.StoreOnboard = value == "1";
                            break;
                        case "autostart":
                            settings.AutoStart = value == "1";
                            break;
                        case "restore_on_exit":
                            settings.RestoreOnExit = value == "1";
                            break;
                        case "panel_x":
                            settings.PanelX = ParseInt(value, -1);
                            break;
                        case "panel_y":
                            settings.PanelY = ParseInt(value, -1);
                            break;
                    }
                }
            }
            catch
            {
                // 配置坏了就用默认值，不影响启动
            }
            return settings;
        }

        public void Save()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 雷蛇鼠标控制器配置");
            builder.AppendLine("dpi_stages=" + string.Join(",", DpiStages.ConvertAll<string>(
                delegate(int v) { return v.ToString(CultureInfo.InvariantCulture); }).ToArray()));
            builder.AppendLine("active_stage=" + ActiveStage.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("polling_hz=" + PollingHz.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("low_battery=" + LowBatteryThreshold.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("store_onboard=" + (StoreOnboard ? "1" : "0"));
            builder.AppendLine("autostart=" + (AutoStart ? "1" : "0"));
            builder.AppendLine("restore_on_exit=" + (RestoreOnExit ? "1" : "0"));
            builder.AppendLine("panel_x=" + PanelX.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("panel_y=" + PanelY.ToString(CultureInfo.InvariantCulture));

            try
            {
                Directory.CreateDirectory(FolderPath);
                File.WriteAllText(FilePath, builder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 写配置失败不该影响使用
            }
        }

        private static List<int> ParseStages(string value)
        {
            var result = new List<int>();
            foreach (string part in value.Split(','))
            {
                int stage;
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stage))
                    result.Add(stage);
            }
            return result;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return fallback;
        }
    }
}
