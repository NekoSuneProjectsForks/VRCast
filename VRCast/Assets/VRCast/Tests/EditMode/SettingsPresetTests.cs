using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 設定プリセット（.yaml・.json・以前の .vrcastpreset）の書き出し・解析・適用と、YAML の変換・JSON のキー単位の切り分けを検証する。
    /// </summary>
    public class SettingsPresetTests
    {
        private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 10, 10, 8, 30, 0, TimeSpan.FromHours(9));

        private static string[] AllIds => SettingsPreset.Categories.Select(category => category.Id).ToArray();

        private static string Export(AppSettings settings, params string[] ids)
        {
            return SettingsPreset.Export(settings, ids.Length > 0 ? ids : AllIds, "1.2.3", CreatedAt);
        }

        private static PresetFile Parse(string text)
        {
            Assert.That(SettingsPreset.TryParse(text, out PresetFile preset, out PresetError error), Is.True,
                $"parse failed: {error}");
            return preset;
        }

        private static AppSettings Changed()
        {
            // 既定値と違う値を各カテゴリに入れた設定
            return new AppSettings
            {
                lowLoadMode = false,
                trackerMode = TrackerMode.Eco,
                backgroundColor = Color.green,
                idleBreathing = 0.5f,
                micGain = 2f,
                trackingMirror = false,
                spoutEnabled = true,
                resetHotkeysInBackground = false,
                uiScale = 1.5f,
                lightIntensity = 2f,
                poseArmDown = 0.4f,
                expressionSmile = "Joy \"big\"",
                thresholdSmile = 0.5f,
                windowWidth = 999,
                microphoneDevice = "My Mic",
                trackingPort = 12000,
            };
        }

        [TestCase(@"C:\a\VRCast-preset.json", true)]
        [TestCase(@"C:\a\VRCast-preset.yaml", true)]
        [TestCase(@"C:\a\VRCast-preset.YML", true)]
        [TestCase(@"C:\a\Old.VRCASTPRESET", true)]
        [TestCase(@"C:\a\avatar.vrm", false)]
        [TestCase(@"C:\a\json", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void HasImportExtension_AcceptsYamlJsonAndLegacy(string path, bool expected)
        {
            // 書き出す .yaml・.json と .yml・以前の版の .vrcastpreset を（大文字・小文字を問わず）読み込み対象にすること
            Assert.That(SettingsPreset.HasImportExtension(path), Is.EqualTo(expected));
        }

        [Test]
        public void EveryField_IsInExactlyOneCategoryOrExcluded()
        {
            // AppSettings の保存されるフィールドを全て集める
            var fields = typeof(AppSettings)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(field => !field.IsNotSerialized)
                .Select(field => field.Name)
                .ToList();

            // それぞれがちょうど 1 か所（いずれかのカテゴリか除外）に入っていること（設定の追加時に割り当て忘れを防ぐ。外れたものはまとめて出す）
            var wrong = fields.Where(field => SettingsPreset.Categories.Count(category => category.Fields.Contains(field))
                + (SettingsPreset.Excluded.Contains(field) ? 1 : 0) != 1).ToList();
            Assert.That(wrong, Is.Empty, "must be in exactly one preset category or Excluded: " + string.Join(", ", wrong));

            // カテゴリ・除外に、存在しないフィールド名が無いこと
            foreach (string name in SettingsPreset.Categories.SelectMany(category => category.Fields)
                         .Concat(SettingsPreset.Excluded))
            {
                Assert.That(fields, Does.Contain(name));
            }
        }

        [Test]
        public void ExportThenApply_RoundTripsValuesAndKeepsExcluded()
        {
            string text = Export(Changed());

            // 既定の設定へ全カテゴリを反映する
            var target = new AppSettings { windowWidth = 640, microphoneDevice = "Other", trackingPort = 13000 };
            Assert.That(SettingsPreset.Apply(Parse(text), AllIds, target), Is.True);

            // 各カテゴリの値が戻ること
            Assert.That(target.lowLoadMode, Is.False);
            Assert.That(target.trackerMode, Is.EqualTo(TrackerMode.Eco));
            Assert.That(target.backgroundColor, Is.EqualTo(Color.green));
            Assert.That(target.idleBreathing, Is.EqualTo(0.5f));
            Assert.That(target.micGain, Is.EqualTo(2f));
            Assert.That(target.trackingMirror, Is.False);
            Assert.That(target.spoutEnabled, Is.True);
            Assert.That(target.resetHotkeysInBackground, Is.False);
            Assert.That(target.uiScale, Is.EqualTo(1.5f));
            Assert.That(target.lightIntensity, Is.EqualTo(2f));
            Assert.That(target.poseArmDown, Is.EqualTo(0.4f));
            Assert.That(target.expressionSmile, Is.EqualTo("Joy \"big\""));
            Assert.That(target.thresholdSmile, Is.EqualTo(0.5f));

            // 除外した値（ウィンドウ・PC 固有）は書き出されず、反映先の値のままであること
            Assert.That(text, Does.Not.Contain("windowWidth"));
            Assert.That(text, Does.Not.Contain("microphoneDevice"));
            Assert.That(text, Does.Not.Contain("avatarCameras"));
            Assert.That(target.windowWidth, Is.EqualTo(640));
            Assert.That(target.microphoneDevice, Is.EqualTo("Other"));
            Assert.That(target.trackingPort, Is.EqualTo(13000));
        }

        [Test]
        public void Export_WritesHeaderAndOnlySelectedCategories()
        {
            string text = Export(Changed(), "performance");
            PresetFile preset = Parse(text);

            // 見出しが読め、選んだカテゴリだけが入っていること
            Assert.That(preset.FormatVersion, Is.EqualTo(SettingsPreset.CurrentFormatVersion));
            Assert.That(preset.AppVersion, Is.EqualTo("1.2.3"));
            Assert.That(preset.CreatedAt, Is.EqualTo("2026-10-10T08:30:00+09:00"));
            Assert.That(preset.Categories.Select(category => category.Id), Is.EqualTo(new[] { "performance" }));
            Assert.That(text, Does.Not.Contain("lightIntensity"));
        }

        [Test]
        public void Apply_UnselectedCategories_KeepCurrentValues()
        {
            PresetFile preset = Parse(Export(Changed()));
            var target = new AppSettings();
            var defaults = new AppSettings();

            // 動作最適化だけを反映する
            Assert.That(SettingsPreset.Apply(preset, new[] { "performance" }, target), Is.True);

            // 選んだカテゴリは変わり、ほかは既定のままであること
            Assert.That(target.trackerMode, Is.EqualTo(TrackerMode.Eco));
            Assert.That(target.uiScale, Is.EqualTo(defaults.uiScale));
            Assert.That(target.lightIntensity, Is.EqualTo(defaults.lightIntensity));
        }

        [Test]
        public void Apply_WithoutAvatarRelated_KeepsLookAndExpressionMapping()
        {
            PresetFile preset = Parse(Export(Changed()));
            var target = new AppSettings();
            var defaults = new AppSettings();

            // アバター関連以外を全て反映する
            string[] ids = SettingsPreset.Categories.Where(category => !category.IsAvatarRelated)
                .Select(category => category.Id).ToArray();
            Assert.That(SettingsPreset.Apply(preset, ids, target), Is.True);

            // ライト・ポーズ・表情の割り当てとしきい値は変わらないこと
            Assert.That(target.lightIntensity, Is.EqualTo(defaults.lightIntensity));
            Assert.That(target.poseArmDown, Is.EqualTo(defaults.poseArmDown));
            Assert.That(target.expressionSmile, Is.EqualTo(defaults.expressionSmile));
            Assert.That(target.thresholdSmile, Is.EqualTo(defaults.thresholdSmile));
            Assert.That(target.uiScale, Is.EqualTo(1.5f));
        }

        [Test]
        public void Parse_HandEditedFile_IgnoresUnknownMissingAndForeignKeys()
        {
            // 手で編集したファイル: 知らないカテゴリ・知らないキー・別カテゴリのキー・除外のキー、項目を消したブロック
            const string text = @"{
                ""format"": ""VRCastPreset"",
                ""formatVersion"": 1,
                ""futureHeader"": { ""a"": [1, 2, ""}""] },
                ""categories"": {
                    ""performance"": {
                        ""trackerMode"": 0,
                        ""futureSetting"": true,
                        ""uiScale"": 2.0,
                        ""windowWidth"": 64
                    },
                    ""futureCategory"": { ""x"": 1 }
                }
            }";
            PresetFile preset = Parse(text);
            Assert.That(preset.Categories.Select(category => category.Id), Is.EqualTo(new[] { "performance" }));

            var target = new AppSettings { lowLoadMode = false, windowWidth = 800 };
            Assert.That(SettingsPreset.Apply(preset, AllIds, target), Is.True);

            // 書いてある項目だけが変わり、消した項目（lowLoadMode）は今の値のまま、ほかのカテゴリ・除外のキーは反映されないこと
            Assert.That(target.trackerMode, Is.EqualTo(TrackerMode.Smooth));
            Assert.That(target.lowLoadMode, Is.False);
            Assert.That(target.uiScale, Is.EqualTo(new AppSettings().uiScale));
            Assert.That(target.windowWidth, Is.EqualTo(800));
        }

        [Test]
        public void Parse_NewerFormat_IsReadableAndFlagged()
        {
            string text = Export(Changed(), "performance").Replace("\"formatVersion\": 1", "\"formatVersion\": 99");

            // 新しい形式でも読め、新しい形式であることがわかること
            PresetFile preset = Parse(text);
            Assert.That(preset.IsNewerFormat, Is.True);
        }

        [TestCase("", PresetError.Malformed)]
        [TestCase("{ not json", PresetError.Malformed)]
        [TestCase("[1, 2]", PresetError.Malformed)]
        [TestCase("{\"windowWidth\": 1}", PresetError.NotPreset)]
        [TestCase("{\"format\": \"Other\", \"formatVersion\": 1, \"categories\": {}}", PresetError.NotPreset)]
        [TestCase("{\"format\": \"VRCastPreset\", \"formatVersion\": 1}", PresetError.Malformed)]
        [TestCase("{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": {\"performance\": [1]}}",
            PresetError.Malformed)]
        [TestCase("{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": {\"unknown\": {\"a\": 1}}}",
            PresetError.Empty)]
        public void Parse_InvalidText_IsRejected(string text, PresetError expected)
        {
            Assert.That(SettingsPreset.TryParse(text, out PresetFile preset, out PresetError error), Is.False);
            Assert.That(preset, Is.Null);
            Assert.That(error, Is.EqualTo(expected));
        }

        [Test]
        public void Parse_TooLargeText_IsRejected()
        {
            string text = new string(' ', (int)SettingsPreset.MaxFileBytes + 1);
            Assert.That(SettingsPreset.TryParse(text, out _, out PresetError error), Is.False);
            Assert.That(error, Is.EqualTo(PresetError.TooLarge));
        }

        [Test]
        public void Apply_OutOfRangeValues_AreClamped()
        {
            // lightIntensity は旧 avatarLook ブロックにあっても graphics へ振り分けられる
            const string text = "{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": " +
                "{\"interface\": {\"uiScale\": 100}, \"avatarLook\": {\"lightIntensity\": -5}}}";
            var target = new AppSettings();

            // 範囲外の値は補正されること
            Assert.That(SettingsPreset.Apply(Parse(text), AllIds, target), Is.True);
            Assert.That(target.uiScale, Is.EqualTo(AppSettings.MaxUiScale));
            Assert.That(target.lightIntensity, Is.EqualTo(0f));
        }

        [Test]
        public void Apply_GraphicsOnly_IgnoresOtherCategoriesInFile()
        {
            string text = Export(Changed());
            var target = new AppSettings { lightIntensity = 1f, uiScale = 1f, poseArmDown = 1f };

            // グラフィックだけ反映し、パネル拡大率やポーズはそのまま
            Assert.That(SettingsPreset.Apply(Parse(text), new[] { SettingsPreset.GraphicsCategoryId }, target), Is.True);
            Assert.That(target.lightIntensity, Is.EqualTo(2f));
            Assert.That(target.backgroundColor, Is.EqualTo(Color.green));
            Assert.That(target.uiScale, Is.EqualTo(1f));
            Assert.That(target.poseArmDown, Is.EqualTo(1f));
        }

        [Test]
        public void Apply_ThemeOnly_DefaultBackgroundFollowsTheme()
        {
            const string text = "{\"format\": \"VRCastPreset\", \"formatVersion\": 1, \"categories\": " +
                "{\"interface\": {\"darkMode\": true}}}";
            var target = new AppSettings();

            // 既定色の背景は、テーマだけを読み込んだときに新しいテーマの既定色になること
            Assert.That(SettingsPreset.Apply(Parse(text), AllIds, target), Is.True);
            Assert.That(target.darkMode, Is.True);
            Assert.That(target.backgroundColor, Is.EqualTo(AppSettings.DarkBackgroundColor));
        }

        [Test]
        public void Apply_NothingSelected_ReturnsFalseAndKeepsSettings()
        {
            PresetFile preset = Parse(Export(Changed()));
            var target = new AppSettings();
            Assert.That(SettingsPreset.Apply(preset, Array.Empty<string>(), target), Is.False);
            Assert.That(target.uiScale, Is.EqualTo(new AppSettings().uiScale));
        }

        [Test]
        public void YamlExport_RoundTripsLikeJson()
        {
            AppSettings source = Changed();
            source.resetHotkeys[0].virtualKey = 0x41;
            source.resetHotkeys[0].ctrl = true;
            string yaml = SettingsPreset.Export(source, AllIds, "1.2.3", CreatedAt, PresetFormat.Yaml);
            string json = SettingsPreset.Export(source, AllIds, "1.2.3", CreatedAt, PresetFormat.Json);

            // YAML はコメントの見出しで始まり、括弧の無いブロック形式で書かれること
            Assert.That(yaml, Does.StartWith("# VRCast 設定プリセット / VRCast settings preset\n"));
            Assert.That(yaml, Does.Contain("\nformat: VRCastPreset\n"));

            // 全カテゴリの前に日本語 / 英語の説明のコメントが付くこと
            foreach (PresetCategory category in SettingsPreset.Categories)
            {
                Assert.That(yaml, Does.Match($@"\n  # [^\n]+ / [^\n]+\n  {category.Id}:\n"), category.Id);
            }
            Assert.That(yaml, Does.Contain("\n    backgroundColor:\n      r: "));
            Assert.That(yaml, Does.Contain("\n    resetHotkeys:\n      - virtualKey: 65\n        ctrl: true\n"));

            // 見出しが読め、YAML から反映した設定は JSON から反映した設定と同じになること（色・キーの一覧・引用符入りの文字列も）
            PresetFile preset = Parse(yaml);
            Assert.That(preset.AppVersion, Is.EqualTo("1.2.3"));
            Assert.That(preset.CreatedAt, Is.EqualTo("2026-10-10T08:30:00+09:00"));
            var fromYaml = new AppSettings();
            var fromJson = new AppSettings();
            Assert.That(SettingsPreset.Apply(preset, AllIds, fromYaml), Is.True);
            Assert.That(SettingsPreset.Apply(Parse(json), AllIds, fromJson), Is.True);
            Assert.That(JsonUtility.ToJson(fromYaml), Is.EqualTo(JsonUtility.ToJson(fromJson)));
            Assert.That(fromYaml.expressionSmile, Is.EqualTo("Joy \"big\""));
            Assert.That(fromYaml.backgroundColor, Is.EqualTo(Color.green));
            Assert.That(fromYaml.resetHotkeys[0].virtualKey, Is.EqualTo(0x41));
            Assert.That(fromYaml.resetHotkeys[0].ctrl, Is.True);
        }

        [Test]
        public void Parse_HandEditedYaml_AcceptsCommentsQuotesAndFlowValues()
        {
            // 手で書いた YAML: コメント・引用符の有無・1 行の { }・知らないキー・中身を消したカテゴリ・CRLF
            const string text =
                "# my preset\r\n" +
                "format: VRCastPreset   # header\r\n" +
                "formatVersion: 1\r\n" +
                "futureHeader: [1, \"]\", {a: b}]\r\n" +
                "categories:\r\n" +
                "  display:\r\n" +
                "    backgroundColor: {r: 0, g: 0, b: 1, a: 1}\r\n" +
                "    cameraLocked: True\r\n" +
                "  face:\r\n" +
                "  interface:\r\n" +
                "    uiScale: +1.25\r\n" +
                "    futureSetting: yes\r\n" +
                "  expressionMapping:\r\n" +
                "    expressionSmile: にっこり #1\r\n" +
                "    expressionSad: 'it''s # sad'\r\n" +
                "    thresholdSmile: .5\r\n";
            PresetFile preset = Parse(text);
            // 背景は graphics、カメラ固定は display へ振り分けられること（以前はどちらも display）
            Assert.That(preset.Categories.Select(category => category.Id),
                Is.EqualTo(new[] { "display", "graphics", "interface", "expressionMapping" }));

            var target = new AppSettings();
            Assert.That(SettingsPreset.Apply(preset, AllIds, target), Is.True);
            Assert.That(target.backgroundColor, Is.EqualTo(Color.blue));
            Assert.That(target.cameraLocked, Is.True);
            Assert.That(target.uiScale, Is.EqualTo(1.25f));
            Assert.That(target.expressionSmile, Is.EqualTo("にっこり"));
            Assert.That(target.expressionSad, Is.EqualTo("it's # sad"));
            Assert.That(target.thresholdSmile, Is.EqualTo(0.5f));
        }

        [TestCase("format: VRCastPreset\nformatVersion: 1\ncategories:\n\tinterface:\n\t\tuiScale: 1")]
        [TestCase("format: VRCastPreset\nformatVersion: 1\ncategories:\n  interface:\n      uiScale: 1\n    darkMode: true")]
        [TestCase("format: VRCastPreset\nformatVersion: 1\ncategories:\n  interface:\n    uiLanguage: \"ja")]
        [TestCase("format: VRCastPreset\nformatVersion: 1\ncategories:\n  interface:\n    uiLanguage: |\n      ja")]
        [TestCase("format: VRCastPreset\nformatVersion: 1\ncategories:\n  interface: {uiScale: 1")]
        [TestCase("just some text\nmore text")]
        public void Parse_BrokenYaml_IsMalformed(string text)
        {
            // タブの字下げ・字下げのずれ・閉じていない引用符や括弧・複数行の文字列・ただのテキストは壊れたものとして扱うこと
            Assert.That(SettingsPreset.TryParse(text, out _, out PresetError error), Is.False);
            Assert.That(error, Is.EqualTo(PresetError.Malformed));
        }

        [Test]
        public void Parse_YamlWithoutHeader_IsNotPreset()
        {
            Assert.That(SettingsPreset.TryParse("windowWidth: 1\n", out _, out PresetError error), Is.False);
            Assert.That(error, Is.EqualTo(PresetError.NotPreset));
        }

        [TestCase("true")]
        [TestCase("No")]
        [TestCase("123")]
        [TestCase("-1")]
        [TestCase("a: b")]
        [TestCase("#tag")]
        [TestCase("x #y")]
        [TestCase("[x]")]
        [TestCase(" padded ")]
        [TestCase("")]
        [TestCase("line\nbreak")]
        [TestCase("にっこり")]
        [TestCase("Smile 2")]
        public void SimpleYaml_StringsRoundTrip(string value)
        {
            // 数値・真偽値に見える文字列や記号入りの文字列も、YAML にして読み戻すと同じ文字列になること
            string json = JsonObjectText.Build(new[] { new KeyValuePair<string, string>("k", JsonObjectText.Quote(value)) });
            Assert.That(SimpleYaml.TryFromJson(json, out string yaml), Is.True);
            Assert.That(SimpleYaml.TryToJson(yaml, out string back), Is.True, yaml);
            Assert.That(JsonObjectText.TryParseObject(back, out var members), Is.True);
            Assert.That(JsonObjectText.TryReadString(members[0].Value, out string read), Is.True, yaml);
            Assert.That(read, Is.EqualTo(value), yaml);
        }

        [Test]
        public void SimpleYaml_NestedSequencesRoundTrip()
        {
            // 入れ子の配列・オブジェクトの配列・空の入れ子が、YAML にして読み戻しても同じ構造になること
            const string json = "{\"a\": [1, [2, 3], {\"b\": true, \"c\": [\"x\"]}, {}, []], \"d\": {}, \"e\": null}";
            Assert.That(SimpleYaml.TryFromJson(json, out string yaml), Is.True);
            Assert.That(SimpleYaml.TryToJson(yaml, out string back), Is.True, yaml);
            Assert.That(Normalize(back), Is.EqualTo(Normalize(json)), yaml);
        }

        private static string Normalize(string json)
        {
            return new string(json.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        [Test]
        public void JsonObjectText_SplitsTopLevelWithEscapesAndNesting()
        {
            const string json = "{ \"a\" : \"x\\\"},{\" , \"b\": {\"c\": [1, {\"d\": \"]\"}]}, \"e\": -1.5e3, \"f\": null }";
            Assert.That(JsonObjectText.TryParseObject(json, out List<KeyValuePair<string, string>> members), Is.True);

            // 文字列内の括弧・エスケープや入れ子に惑わされず、一番外側の 4 件に分かれること
            Assert.That(members.Select(member => member.Key), Is.EqualTo(new[] { "a", "b", "e", "f" }));
            Assert.That(members[0].Value, Is.EqualTo("\"x\\\"},{\""));
            Assert.That(members[1].Value, Is.EqualTo("{\"c\": [1, {\"d\": \"]\"}]}"));
            Assert.That(members[2].Value, Is.EqualTo("-1.5e3"));
            Assert.That(JsonObjectText.TryReadString(members[0].Value, out string a), Is.True);
            Assert.That(a, Is.EqualTo("x\"},{"));
        }

        [TestCase("{\"a\": 1,}")]
        [TestCase("{\"a\": [1}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{\"a\": 1} trailing")]
        [TestCase("{\"a\": \"unterminated}")]
        public void JsonObjectText_Malformed_ReturnsFalse(string json)
        {
            Assert.That(JsonObjectText.TryParseObject(json, out _), Is.False);
        }

        [Test]
        public void JsonObjectText_BuildThenParse_RoundTrips()
        {
            var members = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("k\"1", "true"),
                new KeyValuePair<string, string>("k2", JsonObjectText.Quote("line\nbreak")),
            };

            // 結合したテキストを切り分けると元のキー・値に戻ること
            Assert.That(JsonObjectText.TryParseObject(JsonObjectText.Build(members, 1), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(members));
            Assert.That(JsonObjectText.Build(new List<KeyValuePair<string, string>>()), Is.EqualTo("{}"));
        }
    }
}
