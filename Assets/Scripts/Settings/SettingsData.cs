using System;
using AutoSurvival.Audio;

namespace AutoSurvival.Settings
{
    /// <summary>플레이어 설정 (저장되는 값). JsonUtility로 저장하므로 공개 필드만 쓴다.</summary>
    [Serializable]
    public class SettingsData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int masterPercent = 100;
        public int sfxPercent = 100;
        public int ambientPercent = 70;
        public int shakePercent = 100;      // 카메라 흔들림 세기. 0이면 흔들리지 않는다
        public bool hitstopEnabled = true;  // 큰 충격 때 순간 정지

        public static SettingsData Default() => new SettingsData();

        public SettingsData Clone() => (SettingsData)MemberwiseClone();

        /// <summary>범위를 벗어난 값을 바로잡는다 (손으로 고친 파일, 오래된 버전 등).</summary>
        public SettingsData Sanitize()
        {
            masterPercent = Clamp(masterPercent);
            sfxPercent = Clamp(sfxPercent);
            ambientPercent = Clamp(ambientPercent);
            shakePercent = Clamp(shakePercent);
            version = CurrentVersion;
            return this;
        }

        private static int Clamp(int v) => Math.Max(0, Math.Min(100, v));

        // 실제 소리 크기 (곡선 적용). 오디오 코드는 이 값만 쓰면 된다.
        public float MasterGain => AudioMath.PercentToGain(masterPercent);
        public float SfxGain => AudioMath.PercentToGain(sfxPercent);
        public float AmbientGain => AudioMath.PercentToGain(ambientPercent);
        public float Shake01 => shakePercent / 100f;      // 흔들림은 곡선 없이 선형

        public override bool Equals(object obj) =>
            obj is SettingsData o && o.masterPercent == masterPercent && o.sfxPercent == sfxPercent && o.ambientPercent == ambientPercent
            && o.shakePercent == shakePercent && o.hitstopEnabled == hitstopEnabled;
        public override int GetHashCode() => HashCode.Combine(masterPercent, sfxPercent, ambientPercent, shakePercent, hitstopEnabled);
    }

    /// <summary>설정 <-> 문자열. 깨졌거나 비어 있으면 기본값을 돌려준다 (설정 파일 때문에 게임이 멈추지 않도록).</summary>
    public static class SettingsCodec
    {
        public static string ToJson(SettingsData d) => UnityEngine.JsonUtility.ToJson(d.Clone().Sanitize());

        public static SettingsData Parse(string json, out bool usedDefaults)
        {
            usedDefaults = false;
            if (string.IsNullOrWhiteSpace(json)) { usedDefaults = true; return SettingsData.Default(); }
            try
            {
                var d = UnityEngine.JsonUtility.FromJson<SettingsData>(json);
                if (d == null) { usedDefaults = true; return SettingsData.Default(); }
                return d.Sanitize();
            }
            catch (Exception)
            {
                usedDefaults = true;
                return SettingsData.Default();
            }
        }
    }
}
