using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using AutoSurvival.Core;
using AutoSurvival.Survival;

namespace AutoSurvival.Save
{
    // ───────── 저장소 추상화: 파일(실제) / 메모리(테스트) ─────────

    public interface ISaveStorage
    {
        bool Exists(string key);
        string Read(string key);            // 없으면 null
        string ReadBackup(string key);      // 직전 저장본 (없으면 null)
        void Write(string key, string text);
        void Delete(string key);
    }

    /// <summary>
    /// persistentDataPath/saves/{key}.sav 에 저장. 쓰다가 꺼져도 기존 파일이 깨지지 않도록
    /// 임시 파일(.tmp)에 먼저 쓴 뒤 교체하고, 교체 전 파일은 .bak로 남긴다.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _dir;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public FileSaveStorage(string directory = null)
        {
            _dir = directory ?? Path.Combine(Application.persistentDataPath, "saves");
            Directory.CreateDirectory(_dir);
        }

        private string PathOf(string key) => Path.Combine(_dir, key + ".sav");

        public bool Exists(string key) => File.Exists(PathOf(key));

        public string Read(string key) => ReadFile(PathOf(key));
        public string ReadBackup(string key) => ReadFile(PathOf(key) + ".bak");

        private static string ReadFile(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Utf8) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Write(string key, string text)
        {
            string path = PathOf(key), tmp = path + ".tmp", bak = path + ".bak";
            File.WriteAllText(tmp, text, Utf8);
            if (File.Exists(path)) File.Replace(tmp, path, bak);
            else File.Move(tmp, path);
        }

        public void Delete(string key)
        {
            string path = PathOf(key);
            foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }
    }

    /// <summary>테스트용. Write 때 이전 값을 백업으로 보관한다.</summary>
    public sealed class MemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> files = new();
        public readonly Dictionary<string, string> backups = new();

        public bool Exists(string key) => files.ContainsKey(key);
        public string Read(string key) => files.TryGetValue(key, out var v) ? v : null;
        public string ReadBackup(string key) => backups.TryGetValue(key, out var v) ? v : null;

        public void Write(string key, string text)
        {
            if (files.TryGetValue(key, out var old)) backups[key] = old;
            files[key] = text;
        }

        public void Delete(string key) { files.Remove(key); backups.Remove(key); }
    }

    // ───────── 파일 형식 ─────────

    /// <summary>
    /// 첫 줄 "AS1:해시8자리" + 줄바꿈 + JSON 본문. 해시는 본문 문자열(UTF-8 바이트)의 FNV-1a 32비트.
    /// 파일이 중간에 잘렸거나 한 글자라도 바뀌면 불러올 때 감지된다.
    /// 주의: 암호학적 서명이 아니므로 '고의로 조작하는 것'을 막지는 못한다 (실수·손상 감지용).
    /// </summary>
    public static class SaveFormat
    {
        public const string Magic = "AS1";

        public static uint Fnv1a32(string text)
        {
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }

        public static string Encode(string json) => $"{Magic}:{Fnv1a32(json):x8}\n{json}";

        public static bool TryDecode(string text, out string json, out string error)
        {
            json = null; error = null;
            if (string.IsNullOrEmpty(text)) { error = "파일이 비어 있습니다."; return false; }

            int nl = text.IndexOf('\n');
            if (nl < 0) { error = "헤더가 없습니다."; return false; }

            string header = text.Substring(0, nl).TrimEnd('\r');
            string body = text.Substring(nl + 1);
            if (!header.StartsWith(Magic + ":", StringComparison.Ordinal)) { error = "알 수 없는 파일 형식입니다."; return false; }

            string expected = header.Substring(Magic.Length + 1);
            string actual = Fnv1a32(body).ToString("x8");
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) { error = "파일이 손상되었습니다 (검사값 불일치)."; return false; }

            json = body;
            return true;
        }
    }

    // ───────── 정리·버전 업그레이드 ─────────

    public static class SaveMigration
    {
        /// <summary>
        /// 버전 확인 + 값 정리. 손으로 고친 파일이나 오래된 파일에서도 게임이 깨지지 않도록
        /// null 목록 보정, 범위 제한, 중복 제거를 한다. 새 버전의 파일은 거부한다.
        /// </summary>
        public static bool Upgrade(SaveData d, out string error)
        {
            error = null;
            if (d == null) { error = "데이터를 읽을 수 없습니다."; return false; }
            if (d.version > SaveData.CurrentVersion) { error = "더 새로운 버전의 게임에서 만든 저장 파일입니다."; return false; }

            // v0 -> v1: 목록 필드가 없던 시절의 파일 보정 (이후 버전은 여기에 단계를 추가)
            if (d.version < 1) d.version = 1;

            Sanitize(d);
            d.version = SaveData.CurrentVersion;
            return true;
        }

        public static void Sanitize(SaveData d)
        {
            d.player ??= new PlayerSave();
            d.player.stats ??= new List<StatSave>();
            d.player.inventory ??= new InventorySave();
            d.islands ??= new List<IslandSave>();
            d.concepts ??= new List<ConceptSave>();
            d.oopiSeen ??= new List<OopiSeenSave>();

            d.playTimeSeconds = Mathf.Max(0f, d.playTimeSeconds);
            d.deaths = Mathf.Max(0, d.deaths);

            var inv = d.player.inventory;
            inv.wood = Mathf.Max(0, inv.wood); inv.stone = Mathf.Max(0, inv.stone); inv.metalPanel = Mathf.Max(0, inv.metalPanel);
            inv.water = Mathf.Max(0, inv.water); inv.food = Mathf.Max(0, inv.food);

            foreach (var s in d.player.stats)
            {
                s.current = Mathf.Max(0f, s.current);
                if (s.type == StatType.Health) s.current = Mathf.Max(1f, s.current);   // 체력 0으로 저장된 파일로 곧바로 사망하지 않도록
            }

            // 섬 중복 제거(첫 번째 유지) + 기계 정리
            var seenIslands = new HashSet<string>();
            d.islands.RemoveAll(i => i == null || string.IsNullOrEmpty(i.id) || !seenIslands.Add(i.id));
            foreach (var isl in d.islands)
            {
                isl.machines ??= new List<MachineSave>();
                var seen = new HashSet<string>();
                isl.machines.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id) || !seen.Add(m.id));
                foreach (var m in isl.machines) m.tier = Mathf.Clamp(m.tier, 1, 5);
                if (isl.cleared) isl.unlocked = true;
            }

            // 개념 기록: 성공 <= 시도, 음수 금지, 같은 개념 중복은 합친다
            var merged = new Dictionary<ConceptTag, ConceptSave>();
            foreach (var c in d.concepts)
            {
                if (c == null) continue;
                c.attempts = Mathf.Max(0, c.attempts);
                c.successes = Mathf.Clamp(c.successes, 0, c.attempts);
                c.secondsSinceSeen = Mathf.Max(0f, c.secondsSinceSeen);
                if (merged.TryGetValue(c.concept, out var prev))
                {
                    prev.attempts += c.attempts; prev.successes += c.successes;
                    prev.secondsSinceSeen = Mathf.Min(prev.secondsSinceSeen, c.secondsSinceSeen);
                }
                else merged[c.concept] = c;
            }
            d.concepts = new List<ConceptSave>(merged.Values);

            d.oopiSeen.RemoveAll(o => o == null || string.IsNullOrEmpty(o.lineId) || o.count <= 0);
        }
    }

    // ───────── 슬롯 저장/불러오기 ─────────

    public sealed class LoadResult
    {
        public bool ok;
        public SaveData data;
        public string error;
        public bool usedBackup;     // 본 파일이 손상되어 직전 저장본으로 복구했는가
    }

    public sealed class SlotSummary
    {
        public int slot;
        public bool exists, valid;
        public bool usedBackup;         // 본 파일은 손상되었지만 직전 저장본으로 불러올 수 있음
        public string islandId, savedAtUtc, error;
        public float playTimeSeconds;
        public bool gameComplete;
    }

    /// <summary>슬롯 0 = 자동 저장, 1~3 = 수동 저장.</summary>
    public sealed class SaveSystem
    {
        public const int AutoSlot = 0;
        public const int ManualSlots = 3;

        private readonly ISaveStorage _storage;
        public SaveSystem(ISaveStorage storage) { _storage = storage; }

        public static string KeyOf(int slot) => slot == AutoSlot ? "autosave" : $"slot{slot}";

        public bool HasSave(int slot) => _storage.Exists(KeyOf(slot));

        public void Delete(int slot) => _storage.Delete(KeyOf(slot));

        public bool Save(int slot, SaveData data, out string error)
        {
            error = null;
            try
            {
                data.version = SaveData.CurrentVersion;
                data.savedAtUtc = DateTime.UtcNow.ToString("o");
                string json = JsonUtility.ToJson(data, false);
                _storage.Write(KeyOf(slot), SaveFormat.Encode(json));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = $"저장에 실패했습니다: {e.Message}";
                return false;
            }
        }

        public LoadResult Load(int slot)
        {
            string key = KeyOf(slot);
            string text = _storage.Read(key);
            if (text == null)
            {
                // 본 파일이 사라졌어도 백업이 있으면 시도
                string onlyBackup = _storage.ReadBackup(key);
                if (onlyBackup == null) return new LoadResult { error = "저장 파일이 없습니다." };
                var fromBackup = Parse(onlyBackup);
                fromBackup.usedBackup = fromBackup.ok;
                return fromBackup;
            }

            var primary = Parse(text);
            if (primary.ok) return primary;

            // 본 파일이 손상: 직전 저장본으로 복구 시도
            string backup = _storage.ReadBackup(key);
            if (backup != null)
            {
                var b = Parse(backup);
                if (b.ok) { b.usedBackup = true; return b; }
            }
            return primary;
        }

        private static LoadResult Parse(string text)
        {
            var result = new LoadResult();
            if (!SaveFormat.TryDecode(text, out string json, out string error)) { result.error = error; return result; }

            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(json); }
            catch (ArgumentException e) { result.error = $"내용을 읽을 수 없습니다: {e.Message}"; return result; }

            if (!SaveMigration.Upgrade(data, out error)) { result.error = error; return result; }

            result.ok = true; result.data = data;
            return result;
        }

        /// <summary>슬롯 선택 화면용 요약 (자동 저장 + 수동 3칸).</summary>
        public List<SlotSummary> ListSlots()
        {
            var list = new List<SlotSummary>();
            for (int slot = 0; slot <= ManualSlots; slot++)
            {
                var s = new SlotSummary { slot = slot, exists = HasSave(slot) };
                if (s.exists)
                {
                    var r = Load(slot);
                    s.valid = r.ok;
                    s.usedBackup = r.usedBackup;
                    s.error = r.error;
                    if (r.ok)
                    {
                        s.islandId = r.data.currentIslandId;
                        s.savedAtUtc = r.data.savedAtUtc;
                        s.playTimeSeconds = r.data.playTimeSeconds;
                        s.gameComplete = r.data.gameComplete;
                    }
                }
                list.Add(s);
            }
            return list;
        }
    }
}
