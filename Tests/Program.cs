using System.IO.Compression;
using System.Text;
using System.Runtime.InteropServices;
using System.IO.Hashing;
using System.Xml;
using S6Packer.Source;
using Utility = S6Patcher.Source.Utilities.Utility;
using S6Patcher.Source.Patcher;
using S6Patcher.Source.Utilities;
using GamePatcher = S6Patcher.Source.Patcher.Patcher;

internal static class Program
{
    private static int Count, Failures;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "s6patcher-tests-" + Guid.NewGuid().ToString("N"));

    private static async Task<int> Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            LoggerTests.Run(Test, Root);
            RecoveryTests();
            ParserTests();
            MappingTests();
            ValidationTests();
            OptionsAndPathTests();
            FileDataTests();
            GameplayXmlTests();
            await TransactionTests();
            await ModTests();
            await ArchiveTests();
            await DocumentsTests();
        }
        finally { Directory.Delete(Root, true); }
        Console.WriteLine($"C#: {Count} tests, {Count - Failures} passed, {Failures} failed");
        return Failures == 0 ? 0 : 1;
    }

    private static void Test(string Name, Action Body)
    {
        Count++;
        try { Body(); Console.WriteLine("PASS " + Name); }
        catch (Exception ex) { Failures++; Console.WriteLine("FAIL " + Name + ": " + ex); }
        finally { ErrorTracking.Reset(); IOFileHandler.Instance.DiscardOptionChanges(); }
    }
    private static async Task Test(string Name, Func<Task> Body)
    {
        Count++;
        try { await Body(); Console.WriteLine("PASS " + Name); }
        catch (Exception ex) { Failures++; Console.WriteLine("FAIL " + Name + ": " + ex); }
        finally { ErrorTracking.Reset(); IOFileHandler.Instance.DiscardOptionChanges(); }
    }
    private static void Equal<T>(T Expected, T Actual) where T : notnull
    { if (!EqualityComparer<T>.Default.Equals(Expected, Actual)) throw new Exception($"Expected {Expected}, got {Actual}"); }
    private static void Bytes(byte[] Expected, byte[] Actual)
    { if (!Expected.SequenceEqual(Actual)) throw new Exception("Byte sequences differ"); }
    private static void Throws<T>(Action Body) where T : Exception
    {
        try { Body(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static MemoryStream Compressed(byte[] Raw)
    {
        MemoryStream Output = new();
        using (GZipStream Zip = new(Output, CompressionLevel.Optimal, true)) Zip.Write(Raw);
        Output.Position = 0;
        return Output;
    }
    private static byte[] Definition(byte Version = 1, uint? DeclaredSize = null, ushort? DeclaredLength = null, bool Duplicate = false)
    {
        using MemoryStream Block = new();
        using (BinaryWriter Entry = new(Block, Encoding.UTF8, true))
        {
            for (int i = 0; i < (Duplicate ? 2 : 1); i++)
            { Entry.Write(Encoding.ASCII.GetBytes("TST")); Entry.Write(5U); Entry.Write(DeclaredLength ?? (ushort)2); Entry.Write(new byte[] {1, 2}); }
        }
        using MemoryStream Stream = new();
        using (BinaryWriter Writer = new(Stream, Encoding.UTF8, true))
        { Writer.Write(Encoding.ASCII.GetBytes("EMX")); Writer.Write(Version); Writer.Write((byte)0); Writer.Write(DeclaredSize ?? (uint)Block.Length); Writer.Write(Block.ToArray()); }
        return Stream.ToArray();
    }
    private static void RecoveryTests()
    {
        Test("HE session restores original EXE, scripts, INI and mod", () =>
        {
            string root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Settlers6.exe");
            string ini = Path.Combine(root, "Options.ini");
            string script = Path.Combine(root, "UserScriptGlobal.lua");
            string mod = Path.Combine(root, "shr");
            File.WriteAllText(exe, "original exe"); File.WriteAllText(ini, "original ini");
            Directory.CreateDirectory(mod); File.WriteAllText(Path.Combine(mod, "original"), "original mod");
            var session = PatchSessionRecovery.Begin(exe, [exe, ini, script], [mod]);
            File.WriteAllText(exe, "changed exe"); File.WriteAllText(ini, "changed ini");
            File.WriteAllText(script, "new script");
            Directory.Delete(mod, true); Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "new"), "new mod");
            session.Dispose();
            Equal("original exe", File.ReadAllText(exe));
            Equal("original ini", File.ReadAllText(ini));
            Equal(false, File.Exists(script));
            Equal("original mod", File.ReadAllText(Path.Combine(mod, "original")));
            Equal(false, Directory.Exists(PatchSessionRecovery.JournalPath(exe)));
        });
        Test("HE session recovers after simulated process interruption", () =>
        {
            string root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Settlers6.exe");
            File.WriteAllText(exe, "before");
            _ = PatchSessionRecovery.Begin(exe, [exe], []);
            File.WriteAllText(exe, "after");
            Equal(true, PatchSessionRecovery.Recover(exe));
            Equal("before", File.ReadAllText(exe));
        });
        Test("HE committed session preserves modifications", () =>
        {
            string root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Settlers6.exe");
            File.WriteAllText(exe, "before");
            using (var session = PatchSessionRecovery.Begin(exe, [exe], []))
            {
                File.WriteAllText(exe, "after");
                session.Commit();
            }
            Equal(false, PatchSessionRecovery.Recover(exe));
            Equal("after", File.ReadAllText(exe));
        });
        Test("HE session snapshots original EXE despite exclusive Windows file lock", () =>
        {
            string root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Settlers6.exe");
            File.WriteAllText(exe, "locked original executable");
            using var transaction = new ExecutableTransaction(exe);
            using var session = PatchSessionRecovery.Begin(exe, [exe], [], transaction.CopyOriginalTo);
            Equal("locked original executable", File.ReadAllText(Path.Combine(PatchSessionRecovery.JournalPath(exe), "0000.file")));
            session.Commit();
        });
        Test("HE recovery preflights every backup before mutating files", () =>
        {
            string root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Settlers6.exe");
            string ini = Path.Combine(root, "Options.ini");
            File.WriteAllText(exe, "old exe");
            File.WriteAllText(ini, "old ini");
            _ = PatchSessionRecovery.Begin(exe, [exe, ini], []);
            File.WriteAllText(exe, "new exe");
            File.WriteAllText(ini, "new ini");
            File.Delete(Path.Combine(PatchSessionRecovery.JournalPath(exe), "0000.file"));
            Throws<IOException>(() => PatchSessionRecovery.Recover(exe));
            Equal("new exe", File.ReadAllText(exe));
            Equal("new ini", File.ReadAllText(ini));
            Equal(true, Directory.Exists(PatchSessionRecovery.JournalPath(exe)));
        });
        Test("HE recovery refuses incomplete journal instead of guessing", () =>
        {
            string root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string exe = Path.Combine(root, "Settlers6.exe");
            File.WriteAllText(exe, "original");
            Directory.CreateDirectory(PatchSessionRecovery.JournalPath(exe));
            Throws<InvalidDataException>(() => PatchSessionRecovery.Recover(exe));
            Equal("original", File.ReadAllText(exe));
        });
    }

    private static void ParserTests()
    {
        Test("parse valid synthetic definition", () => { using BinaryParser P = new(Compressed(Definition())); Bytes([1, 2], P.ParseBinaryWrapper(execID.OV, "TST")[5]); });
        Test("reject unknown definition version", () => Throws<Exception>(() => { using BinaryParser P = new(Compressed(Definition(2))); }));
        Test("reject truncated definition block", () => { using BinaryParser P = new(Compressed(Definition(DeclaredSize: 500))); Throws<InvalidDataException>(() => P.ParseBinaryWrapper(execID.OV, "TST")); });
        Test("reject truncated definition entry", () => { using BinaryParser P = new(Compressed(Definition(DeclaredLength: 6))); Throws<InvalidDataException>(() => P.ParseBinaryWrapper(execID.OV, "TST")); });
        Test("reject duplicate definition offsets", () => { using BinaryParser P = new(Compressed(Definition(Duplicate: true))); Throws<InvalidDataException>(() => P.ParseBinaryWrapper(execID.OV, "TST")); });
        Test("missing executable block has explicit failure", () => { using BinaryParser P = new(Compressed(Definition())); Throws<Exception>(() => P.ParseBinaryWrapper(execID.ED, "TST")); });
        Test("reject malformed feature identifier", () => { using BinaryParser P = new(Compressed(Definition())); Throws<InvalidDataException>(() => P.ParseBinaryWrapper(execID.OV, "TOOLONG")); });
        Test("cap decompressed definition size", () => Throws<InvalidDataException>(() => { using BinaryParser P = new(Compressed(new byte[16 * 1024 * 1024 + 1])); }));
        Test("embedded file mappings parse", () => { using BinaryParser P = new("S6Patcher.Definitions.FileData.bin"); Equal(13, P.ParseFileData().Count); });
        Test("reject truncated FileData string", () => { using BinaryParser P = new(Compressed([69,77,88,1,255,255,255,127])); Throws<InvalidDataException>(() => P.ParseFileData()); });
        Test("reject empty magic header", () => Throws<Exception>(() => { using BinaryParser P = new(Compressed([])); }));
    }
    private static Mappings Mapping(execID ID, out BinaryParser Parser)
    { Parser = new BinaryParser("S6Patcher.Definitions.Definitions.bin"); return new Mappings(ID, Parser); }
    private static void MappingTests()
    {
        foreach (execID ID in new[] {execID.OV, execID.HE_STEAM, execID.HE_UBISOFT})
        {
            Test($"{ID} texture markers survive low-byte collisions", () =>
            {
                Mappings M = Mapping(ID, out BinaryParser P); using (P)
                { uint[] Values = M.GetTextureResolutionMapping(257).Values.Select(V => BitConverter.ToUInt32(V)).ToArray(); Equal("257,128,64", string.Join(',', Values)); }
            });
            Test($"{ID} zoom marker targets and lengths", () =>
            { Mappings M = Mapping(ID, out BinaryParser P); using (P) { var Map = M.GetZoomLevelMapping(12000, 12000); Equal(ID == execID.OV ? 8 : 4, Map.Values.First().Length); } });
            Test($"{ID} documents path buffer boundary", () =>
            { Mappings M = Mapping(ID, out BinaryParser P); using (P) { var Map = M.GetDocumentsFolderMapping(new string('a', 89)); Equal(180, Map.Values.Max(V => V.Length)); Throws<ArgumentException>(() => M.GetDocumentsFolderMapping(new string('a', 90))); } });
        }
        Test("mapping generation is repeatable", () =>
        { Mappings M = Mapping(execID.HE_UBISOFT, out BinaryParser P); using (P) { var A = M.GetZoomLevelMapping(12000, 12000); var B = M.GetZoomLevelMapping(12000, 12000); foreach (var E in A) Bytes(E.Value, B[E.Key]); } });
        Test("repeat feature selection is idempotent", () =>
        { Mappings M = Mapping(execID.OV, out BinaryParser P); using (P) { Equal(M.GetMapping(["SBF"]).Count, M.GetMapping(["SBF", "SBF"]).Count); } });
        Test("reject NaN infinite and negative settings", () =>
        {
            Mappings M = Mapping(execID.HE_UBISOFT, out BinaryParser P); using (P)
            { foreach (double V in new[] {double.NaN, double.PositiveInfinity, -1}) { Throws<ArgumentOutOfRangeException>(() => M.GetAutoSaveMapping(V)); Throws<ArgumentOutOfRangeException>(() => M.GetZoomLevelMapping(V, (float)V)); } }
        });
        Test("autosave zero uses disable opcode", () =>
        { Mappings M = Mapping(execID.HE_UBISOFT, out BinaryParser P); using (P) { Equal((byte)0xEB, M.GetAutoSaveMapping(0).Values.First()[0]); Equal((byte)0x76, M.GetAutoSaveMapping(15).Values.First()[0]); } });
        Test("reject documents path with embedded NUL", () =>
        { Mappings M = Mapping(execID.OV, out BinaryParser P); using (P) Throws<ArgumentException>(() => M.GetDocumentsFolderMapping("abc\0def")); });
    }
    private static string Executable(execID ID, string? Name = null)
    {
        string Dir = Path.Combine(Root, Guid.NewGuid().ToString("N"), "Game", "base", "bin");
        Directory.CreateDirectory(Dir);
        string FilePath = Path.Combine(Dir, Name ?? "Settlers6.exe");
        byte[] Image = new byte[0x1000000];
        using MemoryStream Stream = new(Image);
        using BinaryWriter W = new(Stream);
        W.Write((ushort)0x5A4D); Stream.Position = 0x3C; W.Write(0x80U);
        Stream.Position = 0x80; W.Write(0x4550U); W.Write((ushort)0x14C); W.Write((ushort)1);
        Stream.Position = 0x94; W.Write((ushort)0xE0); Stream.Position = 0x98; W.Write((ushort)0x10B);
        uint Offset = ID switch {execID.OV => 0x6ECADC, execID.HE_STEAM => 0xF545A4, execID.HE_UBISOFT => 0xF531A4, _ => 0x6D06A8};
        Stream.Position = Offset; W.Write(Encoding.Unicode.GetBytes("1, 71, 4289, 0"));
        Stream.Position = ID == execID.OV ? 0xC044 : 0x1E0F08; W.Write(new byte[] {0x84,0xC0});
        File.WriteAllBytes(FilePath, Image); return FilePath;
    }
    private static execID Validate(string Path)
    { using FileStream S = new(Path, FileMode.Open, FileAccess.Read); return new Validator(S).Validate(); }
    private static void Overwrite(string Path, long Offset, byte[] Data)
    { using FileStream S = new(Path, FileMode.Open, FileAccess.Write); S.Position = Offset; S.Write(Data); }
    private static void ValidationTests()
    {
        foreach (execID ID in new[] {execID.OV,execID.HE_UBISOFT,execID.HE_STEAM,execID.ED})
            Test($"accept synthetic PE fixture for {ID}", () => Equal(ID, Validate(Executable(ID))));
        Test("reject non-PE with copied version string", () => { string F = Executable(execID.OV); Overwrite(F, 0, [0,0]); Throws<InvalidDataException>(() => Validate(F)); });
        Test("reject x64 image", () => { string F = Executable(execID.OV); Overwrite(F, 0x84, [0x64,0x86]); Throws<InvalidDataException>(() => Validate(F)); });
        Test("reject malformed PE pointer", () => { string F = Executable(execID.OV); Overwrite(F, 0x3C, [255,255,255,255]); Throws<InvalidDataException>(() => Validate(F)); });
        Test("reject ambiguous edition markers", () => { string F = Executable(execID.OV); Overwrite(F, 0x6D06A8, Encoding.Unicode.GetBytes("1, 71, 4289, 0")); Throws<InvalidDataException>(() => Validate(F)); });
        Test("reject packed Steam fixture", () => { string F = Executable(execID.HE_STEAM); Overwrite(F, 0x1E0F08, [0,0]); Throws<Exception>(() => Validate(F)); });
        Test("truncated version marker is rejected without over-read", () =>
        { string F = Executable(execID.OV); using (FileStream S = File.OpenWrite(F)) S.SetLength(0x6ECADD); Throws<InvalidDataException>(() => Validate(F)); });
    }
    private static void OptionsAndPathTests()
    {
        Test("INI edit preserves other sections comments and substring keys", () =>
        {
            List<string> Lines = ["[Other]", "Version=game", "[S6Patcher]", ";Version reminder", "LongVersion=keep", "Version=old", "[Display]", "Version=driver"];
            OptionsFile.Set(Lines, "[S6Patcher]", "Version", "new");
            Equal(true, Lines.Contains("Version=game") && Lines.Contains("Version=driver") && Lines.Contains("LongVersion=keep") && Lines.Contains(";Version reminder"));
            Equal(1, Lines.Count(L => L == "Version=new"));
        });
        Test("INI duplicate keys updated once and persistence is idempotent", () =>
        { List<string> Lines = ["[s6patcher]", "version = old", "VERSION=duplicate"]; OptionsFile.Set(Lines,"[S6Patcher]","Version","new"); var Saved = Lines.ToArray(); OptionsFile.Set(Lines,"[S6Patcher]","Version","new"); Equal(string.Join('\n',Saved),string.Join('\n',Lines)); Equal(2, Lines.Count); });
        Test("repatching preserves existing game feature preferences", () =>
        {
            string Old=UserScriptHandler.Instance.GlobalDocuments;
            string Docs=Path.Combine(Root,"profiles"); string Dir=Path.Combine(Docs,"THE SETTLERS - Rise of an Empire","Config"); Directory.CreateDirectory(Dir);
            string Ini=Path.Combine(Dir,"Options.ini"); File.WriteAllText(Ini,"[S6Patcher]\r\nDayNightCycle=0\r\n[Other]\r\nDayNightCycle=keep\r\n");
            try { UserScriptHandler.Instance.GlobalDocuments=Docs; IOFileHandler.Instance.UpdateEntryInOptionsFile("[S6Patcher]","DayNightCycle","1",true); IOFileHandler.Instance.UpdateEntryInOptionsFile("[S6Patcher]","UseDowngrade","1",true); IOFileHandler.Instance.WriteBackToOptionsFiles(); string Text=File.ReadAllText(Ini); Equal(true,Text.Contains("DayNightCycle=0")&&Text.Contains("DayNightCycle=keep")&&Text.Contains("UseDowngrade=1")); }
            finally { UserScriptHandler.Instance.GlobalDocuments=Old; }
        });
        Test("mismatched or incomplete online scripts cannot replace bundled fixes", () =>
        { Equal(false,UserScriptHandler.Instance.MatchesEmbeddedBundle([new MemoryStream([1]),new MemoryStream([2]),new MemoryStream([3])])); Equal(false,UserScriptHandler.Instance.MatchesEmbeddedBundle([])); });
        Test("matching complete online bundle is accepted", () =>
        {
            List<MemoryStream> Files=[new(S6Patcher.Properties.Resources.UserScriptLocal),new(S6Patcher.Properties.Resources.EMXBinData),new(S6Patcher.Properties.Resources.UserScriptGlobal)];
            try { Equal(true,UserScriptHandler.Instance.MatchesEmbeddedBundle(Files)); } finally { Files.ForEach(F=>F.Dispose()); }
        });
        Test("missing documents directory returns no profiles", () =>
        { string Old = UserScriptHandler.Instance.GlobalDocuments; try { UserScriptHandler.Instance.GlobalDocuments = Path.Combine(Root,"missing"); Equal(0,UserScriptHandler.Instance.GetUserScriptDirectories().Count); } finally { UserScriptHandler.Instance.GlobalDocuments = Old; } });
        Test("Windows relative mod path remains inside root", () => Equal(Path.Combine(Root,"config","logic.xml"), SafePath.InDirectory(Root,"config\\logic.xml")));
        Test("reject Windows and Unix absolute traversal and ADS paths", () =>
        { foreach (string P in new[] {"../evil", "config\\..\\evil", "C:\\evil", "\\\\server\\evil", "/evil", "file:stream", "config/../evil", "config./evil", "evil\0name"}) Throws<InvalidDataException>(() => SafePath.InDirectory(Root,P)); });
        Test("case-insensitive resolution handles missing parent directories", () => Equal(Path.Combine(Root,"missing","child"),Utility.ResolveCaseInsensitivePath(Path.Combine(Root,"missing","child"))));
        Test("too-shallow installation path has explicit error", () => Throws<InvalidDataException>(() => IOFileHandler.Instance.GetModLoaderDirectory(execID.HE_UBISOFT, Path.Combine(Path.GetPathRoot(Root)!,"game.exe"))));
        Test("closing a null stream is harmless", () => IOFileHandler.Instance.CloseStream(null!));
    }
    private static void FileDataTests()
    {
        Test("FileData replace insert delete on synthetic bytes", () =>
        { FileDataEntry E = new() {Data = new() {{1,[0,9]}, {3,[1,8]}, {5,[2,1,0,0,0]}}}; Bytes([0,9,2,8,3,4], FileDataPatches.Apply(E,[0,1,2,3,4,5])); });
        Test("FileData invalid operation and out-of-range replacement reject", () =>
        { foreach (byte[] D in new byte[][] {[3], [0,1,2], [2,1], [2,255,255,255,255]}) { FileDataEntry E = new() {Data=new() {{2,D}}}; Throws<InvalidDataException>(() => FileDataPatches.Apply(E,[0,1,2])); } });
        Test("FileData allows insertion at EOF", () =>
        { FileDataEntry E = new() {Data = new() {{2,[1,7]}}}; Bytes([1,2,7],FileDataPatches.Apply(E,[1,2])); });
    }
    private static void XmlTest(string Name, Action Body) => Test("XML " + Name, () =>
    {
        GameplayModification.ClearModifiableFileData();
        try { Body(); }
        finally { GameplayModification.ClearModifiableFileData(); }
    });
    private static FileDataEntry XmlEntry(string FilePath) => GameplayModification.ModifiableFileData.Single(E => E.FilePath == FilePath);
    private static XmlDocument ApplyXml(FileDataEntry Entry, string Content)
    {
        byte[] Result = GameplayModification.UpdateFileContent(Entry, Encoding.UTF8.GetBytes(Content));
        XmlDocument Document = new();
        using MemoryStream Stream = new(Result);
        Document.Load(Stream);
        return Document;
    }
    private static string XmlValues(XmlDocument Document, string XPath) => string.Join(',', Document.SelectNodes(XPath)!.Cast<XmlNode>().Select(N => N.InnerText));
    private static string XmlItems(string Tag, params uint[] Values) => string.Concat(Values.Select(V => $"<{Tag}>{V}</{Tag}>"));
    private static string XmlColor(uint Red, uint Green, uint Blue) => $"<Color><Red>{Red}</Red><Green>{Green}</Green><Blue>{Blue}</Blue></Color>";
    private static void XmlReject(FileDataEntry Entry, string Content)
    {
        byte[] Source = Encoding.UTF8.GetBytes(Content), Before = Source.ToArray();
        string Destination = Path.Combine(Root, "xml-destination.xml");
        File.WriteAllText(Destination, "previous complete file");
        try
        {
            byte[] Result = GameplayModification.UpdateFileContent(Entry, Source);
            File.WriteAllBytes(Destination, Result);
            throw new Exception("Expected explicit XML failure before destination write");
        }
        catch (InvalidDataException Ex)
        {
            Equal(true, Ex.Message.Contains(Entry.FilePath, StringComparison.Ordinal));
        }
        Bytes(Before, Source);
        Equal("previous complete file", File.ReadAllText(Destination));
    }
    private static void GameplayXmlTests()
    {
        XmlTest("color edits the first two complete records and preserves remaining colors and metadata", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlDocument D = ApplyXml(XmlEntry("config\\playercolor.xml"), "<Colors title='été'>\n<!-- keep -->" + XmlColor(9, 8, 7) + XmlColor(1, 2, 3) + XmlColor(4, 5, 6) + "</Colors>");
            Equal("0,17,4", XmlValues(D, "/Colors/Color/Red"));
            Equal("0,7,5", XmlValues(D, "/Colors/Color/Green"));
            Equal("0,216,6", XmlValues(D, "/Colors/Color/Blue"));
            Equal("été", D.DocumentElement!.GetAttribute("title"));
            Equal(1, D.SelectNodes("/Colors/comment()")!.Count);
        });
        XmlTest("RGB homonyms outside Color are not player channels", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlDocument D = ApplyXml(XmlEntry("config\\playercolor.xml"), "<Colors><Preview><Red>91</Red><Green>92</Green><Blue>93</Blue></Preview>" + XmlColor(9, 8, 7) + XmlColor(1, 2, 3) + "</Colors>");
            Equal("91", XmlValues(D, "/Colors/Preview/Red"));
            Equal("0,17", XmlValues(D, "/Colors/Color/Red"));
            Equal("0,7", XmlValues(D, "/Colors/Color/Green"));
            Equal("0,216", XmlValues(D, "/Colors/Color/Blue"));
        });
        XmlTest("missing selected RGB channel cannot shift values into another player", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlReject(XmlEntry("config\\playercolor.xml"), "<Colors><Color><Red>1</Red><Blue>3</Blue></Color>" + XmlColor(4, 5, 6) + XmlColor(7, 8, 9) + "</Colors>");
        });
        XmlTest("existing default namespace remains compatible", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlDocument D = ApplyXml(XmlEntry("config\\playercolor.xml"), "<Colors xmlns='urn:game'>" + XmlColor(1, 2, 3) + XmlColor(4, 5, 6) + "</Colors>");
            Equal("urn:game", D.DocumentElement!.NamespaceURI);
            Equal("0,17", string.Join(',', D.GetElementsByTagName("Red").Cast<XmlNode>().Select(N => N.InnerText)));
        });
        XmlTest("foreign namespace homonyms are preserved instead of becoming player records", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlDocument D = ApplyXml(XmlEntry("config\\playercolor.xml"), "<Colors xmlns='urn:game'><Color xmlns='urn:other'><Red>91</Red><Green>92</Green><Blue>93</Blue></Color>" + XmlColor(1, 2, 3) + XmlColor(4, 5, 6) + "</Colors>");
            Equal("91", D.GetElementsByTagName("Red", "urn:other")[0]!.InnerText);
            Equal("0,17", string.Join(',', D.GetElementsByTagName("Red", "urn:game").Cast<XmlNode>().Select(N => N.InnerText)));
        });
        XmlTest("duplicate RGB channel is ambiguous", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlReject(XmlEntry("config\\playercolor.xml"), "<Colors><Color><Red>1</Red><Red>2</Red><Green>3</Green><Blue>4</Blue></Color>" + XmlColor(5, 6, 7) + "</Colors>");
        });
        XmlTest("two Color collections with identical ancestor names are ambiguous", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlReject(XmlEntry("config\\playercolor.xml"), "<Root><Colors>" + XmlColor(1, 2, 3) + "</Colors><Colors>" + XmlColor(4, 5, 6) + "</Colors></Root>");
        });
        XmlTest("RGB values outside a Color record cannot replace missing records", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlReject(XmlEntry("config\\playercolor.xml"), "<Colors><Red>1</Red><Green>2</Green><Blue>3</Blue><Red>4</Red><Green>5</Green><Blue>6</Blue></Colors>");
        });
        XmlTest("a structured RGB target cannot be replaced by scalar text", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            XmlReject(XmlEntry("config\\playercolor.xml"), "<Colors><Color><Red><Value>1</Value></Red><Green>2</Green><Blue>3</Blue></Color>" + XmlColor(4, 5, 6) + "</Colors>");
        });
        XmlTest("settler limits retain the documented five ordinal slots", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            Equal("50,60,120,180,240", XmlValues(ApplyXml(XmlEntry("config\\logic.xml"), "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5) + "</Logic>"), "//SettlerLimit"));
        });
        XmlTest("settler ordinal contract does not invent parent names", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlDocument D = ApplyXml(XmlEntry("config\\logic.xml"), "<Logic><NoCathedral><SettlerLimit>1</SettlerLimit></NoCathedral><Upgrades>" + XmlItems("SettlerLimit", 2, 3, 4, 5) + "</Upgrades></Logic>");
            Equal("50,60,120,180,240", XmlValues(D, "//SettlerLimit"));
        });
        XmlTest("missing final settler slot rejects the whole file", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlReject(XmlEntry("config\\logic.xml"), "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4) + "</Logic>");
        });
        XmlTest("extra settler slot is ambiguous rather than silently ignored", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlReject(XmlEntry("config\\logic.xml"), "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5, 6) + "</Logic>");
        });
        XmlTest("missing all settler slots has an explicit failure", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlReject(XmlEntry("config\\logic.xml"), "<Logic><Other>1</Other></Logic>");
        });
        XmlTest("structured settler limit rejects before changing earlier slots", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlReject(XmlEntry("config\\logic.xml"), "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4) + "<SettlerLimit><Nested>5</Nested></SettlerLimit></Logic>");
        });
        XmlTest("soldier limits update all five castles and ignore other Limit contexts", () =>
        {
            GameplayModification.ModifySoldierLimits([26, 44, 62, 92]);
            Equal(5, GameplayModification.ModifiableFileData.Count);
            foreach (FileDataEntry E in GameplayModification.ModifiableFileData)
            {
                XmlDocument D = ApplyXml(E, "<Entity><Other><Limit>999</Limit></Other><SoldierLimits>" + XmlItems("Limit", 1, 2, 3, 4) + "</SoldierLimits></Entity>");
                Equal("26,44,62,92", XmlValues(D, "/Entity/SoldierLimits/Limit"));
                Equal("999", XmlValues(D, "/Entity/Other/Limit"));
            }
        });
        XmlTest("multiple SoldierLimits cannot be concatenated into four slots", () =>
        {
            GameplayModification.ModifySoldierLimits([26, 44, 62, 92]);
            XmlReject(GameplayModification.ModifiableFileData[0], "<Entity><SoldierLimits>" + XmlItems("Limit", 1, 2) + "</SoldierLimits><SoldierLimits>" + XmlItems("Limit", 3, 4) + "</SoldierLimits></Entity>");
        });
        XmlTest("Limit nodes in the wrong context cannot replace a missing SoldierLimits", () =>
        {
            GameplayModification.ModifySoldierLimits([26, 44, 62, 92]);
            XmlReject(GameplayModification.ModifiableFileData[0], "<Entity><Other>" + XmlItems("Limit", 1, 2, 3, 4) + "</Other></Entity>");
        });
        XmlTest("nested Limit nodes cannot replace direct soldier slots", () =>
        {
            GameplayModification.ModifySoldierLimits([26, 44, 62, 92]);
            XmlReject(GameplayModification.ModifiableFileData[0], "<Entity><SoldierLimits><Wrapper>" + XmlItems("Limit", 1, 2, 3, 4) + "</Wrapper></SoldierLimits></Entity>");
        });
        XmlTest("extra direct soldier slot rejects rather than ignoring it", () =>
        {
            GameplayModification.ModifySoldierLimits([26, 44, 62, 92]);
            XmlReject(GameplayModification.ModifiableFileData[0], "<Entity><SoldierLimits>" + XmlItems("Limit", 1, 2, 3, 4, 5) + "</SoldierLimits></Entity>");
        });
        XmlTest("storehouse limits update one four-slot container and preserve other capacities", () =>
        {
            GameplayModification.ModifyStorehouseLimits([300, 600, 1200, 2400]);
            XmlDocument D = ApplyXml(XmlEntry("config\\entities\\b_storehouse.xml"), "<Entity><Other><OutStockCapacity>999</OutStockCapacity></Other><OutStockCapacities>" + XmlItems("OutStockCapacity", 1, 2, 3, 4) + "</OutStockCapacities></Entity>");
            Equal("300,600,1200,2400", XmlValues(D, "/Entity/OutStockCapacities/OutStockCapacity"));
            Equal("999", XmlValues(D, "/Entity/Other/OutStockCapacity"));
        });
        XmlTest("multiple OutStockCapacities cannot be concatenated into four slots", () =>
        {
            GameplayModification.ModifyStorehouseLimits([300, 600, 1200, 2400]);
            XmlReject(XmlEntry("config\\entities\\b_storehouse.xml"), "<Entity><OutStockCapacities>" + XmlItems("OutStockCapacity", 1, 2) + "</OutStockCapacities><OutStockCapacities>" + XmlItems("OutStockCapacity", 3, 4) + "</OutStockCapacities></Entity>");
        });
        XmlTest("storehouse dependency retains only the first Worth ordinal edit", () =>
        {
            GameplayModification.ModifyStorehouseLimits([300, 600, 1200, 2400]);
            XmlDocument D = ApplyXml(XmlEntry("config\\goodsex.xml"), "<Goods><Good id='first'><Worth>7</Worth></Good><Good id='second'><Worth>8</Worth></Good></Goods>");
            Equal("0,8", XmlValues(D, "//Worth"));
            Equal("second", D.SelectSingleNode("/Goods/Good[2]")!.Attributes!["id"]!.Value);
        });
        XmlTest("missing mandatory Worth does not announce success", () =>
        {
            GameplayModification.ModifyStorehouseLimits([300, 600, 1200, 2400]);
            XmlReject(XmlEntry("config\\goodsex.xml"), "<Goods><Good><Other>7</Other></Good></Goods>");
        });
        XmlTest("structured first Worth cannot lose its child elements", () =>
        {
            GameplayModification.ModifyStorehouseLimits([300, 600, 1200, 2400]);
            XmlReject(XmlEntry("config\\goodsex.xml"), "<Goods><Worth><Amount>7</Amount></Worth><Worth>8</Worth></Goods>");
        });
        XmlTest("malformed XML includes the file in its diagnostic", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlReject(XmlEntry("config\\logic.xml"), "<Logic><SettlerLimit>1</Logic>");
        });
        XmlTest("DTD remains forbidden and its failure includes the file", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            XmlReject(XmlEntry("config\\logic.xml"), "<!DOCTYPE Logic [<!ENTITY value '1'>]><Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5) + "</Logic>");
        });
        XmlTest("out-of-range modification index has an explicit failure", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            FileDataEntry E = XmlEntry("config\\logic.xml"); E.Data.Add(uint.MaxValue, []);
            XmlReject(E, "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5) + "</Logic>");
        });
        XmlTest("modification indices belonging to another file are refused", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            FileDataEntry E = XmlEntry("config\\logic.xml"); E.FilePath = "config\\other.xml";
            XmlReject(E, "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5) + "</Logic>");
        });
        XmlTest("an empty operation mapping cannot report an update", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            FileDataEntry E = XmlEntry("config\\logic.xml"); E.Data.Clear();
            XmlReject(E, "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5) + "</Logic>");
        });
        XmlTest("missing operation mapping cannot silently apply a subset", () =>
        {
            GameplayModification.ModifySettlerLimits([60, 120, 180, 240]);
            FileDataEntry E = XmlEntry("config\\logic.xml"); E.Data.Remove(E.Data.Keys.Max());
            XmlReject(E, "<Logic>" + XmlItems("SettlerLimit", 1, 2, 3, 4, 5) + "</Logic>");
        });
        XmlTest("an entirely missing RGB operation group cannot report partial success", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            FileDataEntry E = XmlEntry("config\\playercolor.xml");
            E.Data.Remove(1); E.Data.Remove(4);
            XmlReject(E, "<Colors>" + XmlColor(1, 2, 3) + XmlColor(4, 5, 6) + "</Colors>");
        });
        XmlTest("duplicate configuration cannot target successive unrelated colors", () =>
        {
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(17, 7, 216));
            GameplayModification.ModifyPlayerColor(Avalonia.Media.Color.FromRgb(18, 8, 217));
            XmlReject(XmlEntry("config\\playercolor.xml"), "<Colors>" + XmlColor(1, 2, 3) + XmlColor(4, 5, 6) + XmlColor(7, 8, 9) + XmlColor(10, 11, 12) + "</Colors>");
        });
        XmlTest("limit registration requires four values before creating file operations", () =>
        {
            foreach (Action<uint[]> Register in new Action<uint[]>[] { GameplayModification.ModifySettlerLimits, GameplayModification.ModifySoldierLimits, GameplayModification.ModifyStorehouseLimits })
            {
                Throws<ArgumentNullException>(() => Register(null!));
                foreach (uint[] Invalid in new uint[][] { [], [1, 2, 3], [1, 2, 3, 4, 5] })
                    Throws<ArgumentException>(() => Register(Invalid));
                Equal(0, GameplayModification.ModifiableFileData.Count);
            }
        });
    }
    private static async Task TransactionTests()
    {
        Test("aborted executable transaction leaves original and no backup", () =>
        { string F = Executable(execID.OV); byte[] Before = File.ReadAllBytes(F); using (ExecutableTransaction T = new(F)) Overwrite(T.WorkingPath,10,[1,2]); Bytes(Before,File.ReadAllBytes(F)); Equal(false,File.Exists(Backup.GetBackupPath(F,false))); });
        Test("commit creates pristine backup and patches only staged bytes", () =>
        { string F = Executable(execID.OV); byte[] Before = File.ReadAllBytes(F); using (ExecutableTransaction T = new(F)) { Overwrite(T.WorkingPath,10,[1,2]); T.Commit(); } Bytes(Before,File.ReadAllBytes(Backup.GetBackupPath(F,false))); Equal((byte)1,File.ReadAllBytes(F)[10]); });
        Test("repeat commits retain the first backup", () =>
        { string F = Executable(execID.OV); byte[] Before = File.ReadAllBytes(F); for (byte i=1;i<=2;i++) { using ExecutableTransaction T = new(F); Overwrite(T.WorkingPath,10,[i]); T.Commit(); } Bytes(Before,File.ReadAllBytes(Backup.GetBackupPath(F,false))); });
        await Test("restore is repeatable and keeps the backup", async () =>
        { string F = Executable(execID.OV); byte[] Before = File.ReadAllBytes(F); using (ExecutableTransaction T = new(F)) { Overwrite(T.WorkingPath,10,[1]); T.Commit(); } await Backup.Restore(F,execID.OV); await Backup.Restore(F,execID.OV); Bytes(Before,File.ReadAllBytes(F)); Bytes(Before,File.ReadAllBytes(Backup.GetBackupPath(F,false))); });
        await Test("restore refuses a backup from another edition", async () =>
        { string F = Executable(execID.OV); byte[] Before=File.ReadAllBytes(F); File.Copy(Executable(execID.HE_UBISOFT),Backup.GetBackupPath(F,false)); await Backup.Restore(F,execID.OV); Bytes(Before,File.ReadAllBytes(F)); });
        Test("out-of-range patch aborts without extending executable", () =>
        { string F = Executable(execID.HE_UBISOFT); using (FileStream S = File.OpenWrite(F)) S.SetLength(0xF531A4 + 100); byte[] Before = File.ReadAllBytes(F); using GamePatcherScope P = new(F); Throws<InvalidDataException>(() => P.Value.PatchByControlFeatures(["MDL"])); P.Value.Dispose(); Bytes(Before,File.ReadAllBytes(F)); });
        Test("invalid executable selection never creates a backup", () =>
        { string F = Path.Combine(Root,"invalid.exe"); File.WriteAllText(F,"invalid"); Throws<InvalidDataException>(() => new GamePatcher(F)); Equal(false,File.Exists(Backup.GetBackupPath(F,false))); Equal(false,Directory.EnumerateFiles(Root,"invalid.exe.s6patcher-*").Any()); });
        Test("cancelled atomic write preserves destination", () =>
        { string F=Path.Combine(Root,"atomic.txt"); File.WriteAllText(F,"old"); using CancellationTokenSource C=new(); C.Cancel(); Throws<OperationCanceledException>(()=>AtomicFile.WriteAllBytesAsync(F,[1,2],C.Token).GetAwaiter().GetResult()); Equal("old",File.ReadAllText(F)); Equal(false,Directory.EnumerateFiles(Root,"atomic.txt.s6patcher-*").Any()); });
        Test("patcher commit applies a feature and supports repeat application", () =>
        {
            string F=Executable(execID.OV); byte[] Original=File.ReadAllBytes(F);
            for (int i=0;i<2;i++) { using GamePatcherScope P=new(F); P.Value.PatchByControlFeatures(["SBF"]); P.Value.Complete(false); }
            Bytes(Original,File.ReadAllBytes(Backup.GetBackupPath(F,false)));
            Mappings M=Mapping(execID.OV,out BinaryParser B); using (B) foreach (var E in M.GetMapping(["SBF"])) Bytes(E.Value,File.ReadAllBytes(F).AsSpan((int)E.Key,E.Value.Length).ToArray());
        });
        Test("error tracking prevents patcher commit", () =>
        { string F=Executable(execID.OV); byte[] Before=File.ReadAllBytes(F); using GamePatcherScope P=new(F); P.Value.PatchByControlFeatures(["SBF"]); ErrorTracking.Increment(); Throws<IOException>(()=>P.Value.Complete(false)); P.Value.Dispose(); Bytes(Before,File.ReadAllBytes(F)); });
        Test("patcher refuses an incompatible existing backup", () =>
        { string F=Executable(execID.OV); byte[] Before=File.ReadAllBytes(F); File.Copy(Executable(execID.ED),Backup.GetBackupPath(F,false)); using GamePatcherScope P=new(F); P.Value.PatchByControlFeatures(["SBF"]); Throws<InvalidDataException>(()=>P.Value.Complete(false)); P.Value.Dispose(); Bytes(Before,File.ReadAllBytes(F)); });
        if (OperatingSystem.IsWindows())
            Test("Windows checksum native call updates staged PE", () =>
            { string F=Executable(execID.OV); byte[] Before=File.ReadAllBytes(F); using GamePatcherScope P=new(F); P.Value.PatchByControlFeatures(["SBF"]); P.Value.Complete(true); Bytes(Before,File.ReadAllBytes(Backup.GetBackupPath(F,false))); Equal(true,BitConverter.ToUInt32(File.ReadAllBytes(F),0xD8)!=0); });
    }
    private sealed class GamePatcherScope : IDisposable
    { public GamePatcher Value {get;} public GamePatcherScope(string Path) => Value=new(Path); public void Dispose()=>Value.Dispose(); }
    private static async Task ModTests()
    {
        Test("HE mod swap rolls back after injected failure", () =>
        {
            string parent = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            string active = Path.Combine(parent, "shr"), staged = Path.Combine(parent, "staged");
            Directory.CreateDirectory(active); Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(active, "old.txt"), "original");
            File.WriteAllText(Path.Combine(staged, "new.txt"), "replacement");
            Throws<IOException>(() => Mod.InstallStagedDirectory(staged, active,
                () => throw new IOException("injected crash before activation")));
            Equal("original", File.ReadAllText(Path.Combine(active, "old.txt")));
            Equal(false, Directory.Exists(active + ".s6patcher-recovery"));
            Equal(true, Directory.Exists(staged));
        });
        Test("HE mod recovers interrupted directory swap on next run", () =>
        {
            string parent = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            string active = Path.Combine(parent, "shr");
            string previous = active + ".s6patcher-recovery";
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "old.txt"), "original");
            Mod.RecoverInterruptedInstall(active);
            Equal("original", File.ReadAllText(Path.Combine(active, "old.txt")));
            Equal(false, Directory.Exists(previous));
        });
        Test("HE mod refuses ambiguous two-directory recovery state", () =>
        {
            string parent = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            string active = Path.Combine(parent, "shr");
            Directory.CreateDirectory(active);
            Directory.CreateDirectory(active + ".s6patcher-recovery");
            Throws<IOException>(() => Mod.RecoverInterruptedInstall(active));
            Equal(true, Directory.Exists(active));
            Equal(true, Directory.Exists(active + ".s6patcher-recovery"));
        });
        await Test("failed first HE Steam mod install does not create active shr", async () =>
        {
            string F = Executable(execID.HE_STEAM);
            string Dir = IOFileHandler.Instance.GetModLoaderDirectory(execID.HE_STEAM, F);
            string Active = Path.Combine(Dir, "shr");
            Equal(false, Directory.Exists(Active));
            Mod M = new(execID.HE_STEAM, F);
            try { await M.Create(true, false, false); throw new Exception("Expected failure with missing game assets"); }
            catch (IOException) { }
            Equal(false, Directory.Exists(Active));
        });
        await Test("missing mod sources retain previous bugfix files", async () =>
        {
            string F = Executable(execID.HE_UBISOFT); string Dir=IOFileHandler.Instance.GetModLoaderDirectory(execID.HE_UBISOFT,F);
            Directory.CreateDirectory(Path.Combine(Dir,"shr")); File.WriteAllText(Path.Combine(Dir,"shr","sentinel"),"keep");
            Mod M=new(execID.HE_UBISOFT,F);
            try { await M.Create(true,false,false); throw new Exception("Expected mod-generation failure"); } catch (IOException) { }
            Equal("keep",File.ReadAllText(Path.Combine(Dir,"shr","sentinel")));
            Equal(false,Directory.EnumerateDirectories(Dir,"*.dir").Any());
        });
    }

    private static async Task ArchiveTests()
    {
        string Source=Path.Combine(Root,"archive-source"); Directory.CreateDirectory(Path.Combine(Source,"config"));
        string Payload="<config>synthetic archive contents only</config>";
        File.WriteAllText(Path.Combine(Source,"config","test.xml"),Payload); File.WriteAllText(Path.Combine(Source,"safe.txt"),"new");
        string ArchivePath=Path.Combine(Root,"synthetic.bba");
        await Test("pack synthetic BBA and inspect its guarded directory",async ()=>
        { Equal(true,await Archives.PackFolderFilesIntoArchiveFileAsync(Source,ArchivePath)); using FileStream S=File.OpenRead(ArchivePath); Equal(true,ArchiveGuard.ReadEntries(S,Path.Combine(Root,"extract")).Any(E=>E.FileName=="config\\test.xml")); });
        await Test("archive extraction round-trip truncates old output and releases stream",async ()=>
        {
            string Dest=Path.Combine(Root,"extract"); Directory.CreateDirectory(Dest); File.WriteAllText(Path.Combine(Dest,"safe.txt"),new string('x',200));
            Equal(true,await Archives.ExtractArchiveFileToFolderAsync(ArchivePath,Dest,false)); Equal("new",File.ReadAllText(Path.Combine(Dest,"safe.txt")));
            Equal(Payload,File.ReadAllText(Path.Combine(Dest,"config","test.xml"))); Equal(false,IOFileHandler.Instance.AreStreamsOpen());
        });
        await Test("failed packing preserves prior archive",async ()=>
        { byte[] Before=File.ReadAllBytes(ArchivePath); Equal(false,await Archives.PackFolderFilesIntoArchiveFileAsync(Path.Combine(Root,"absent"),ArchivePath)); Bytes(Before,File.ReadAllBytes(ArchivePath)); });
        await Test("corrupt archive directory is rejected before extraction",async ()=>
        { byte[] Data=File.ReadAllBytes(ArchivePath); Data[^1]^=0xFF; string Bad=Path.Combine(Root,"bad.bba"); File.WriteAllBytes(Bad,Data); string Dest=Path.Combine(Root,"bad-extract"); Equal(false,await Archives.ExtractArchiveFileToFolderAsync(Bad,Dest,false)); Equal(false,Directory.Exists(Dest)); });
        await Test("archive traversal is rejected before any output",async ()=>
        {
            string Bad=Path.Combine(Root,"traversal.bba"); File.Copy(ArchivePath,Bad);
            RewriteDirectory(Bad,Data=>
            { byte[] From=Encoding.ASCII.GetBytes("safe.txt"), To=Encoding.ASCII.GetBytes("..\\e.txt"); int Index=Data.AsSpan().IndexOf(From); if(Index<0)throw new Exception("Fixture filename missing"); To.CopyTo(Data,Index); });
            string Dest=Path.Combine(Root,"traversal-extract"); Equal(false,await Archives.ExtractArchiveFileToFolderAsync(Bad,Dest,false)); Equal(false,File.Exists(Path.Combine(Root,"e.txt"))); Equal(false,Directory.Exists(Dest));
        });
        Test("truncated archive does not enter unsafe decoder",()=>
        { using MemoryStream S=new([66,65,70,4]); Throws<InvalidDataException>(()=>ArchiveGuard.ReadEntries(S,Root)); });
    }

    private static void RewriteDirectory(string Path, Action<byte[]> Mutate)
    {
        byte[] Image=File.ReadAllBytes(Path);
        var Header=MemoryMarshal.Read<BBAHeaderDefinition>(Image);
        byte[] DirBytes=Image.AsSpan(BBAHeader.Size,BBADirectoryHeader.Size).ToArray(); Crypt.Decrypt(DirBytes,Header.EncryptionID);
        var Directory=MemoryMarshal.Read<BBADirectoryHeaderDefinition>(DirBytes);
        byte[] Encoded=Image.AsSpan((int)Directory.Offset,(int)Directory.Length).ToArray(); Crypt.Decrypt(Encoded,Directory.EncryptionID);
        var Comp=MemoryMarshal.Read<CompressedHeaderDefinition>(Encoded);
        byte[] Data=BBACompression.Decompress(Encoded.AsSpan(BBACompression.Size,(int)Comp.CompressedSize).ToArray(),(int)Comp.DecompressedSize); Mutate(Data);
        byte[] Zipped=BBACompression.Compress(Data); Comp.CompressedSize=(uint)Zipped.Length;
        int Size=BBACompression.Size+Zipped.Length; byte[] Updated=new byte[(Size+3)/4*4]; MemoryMarshal.Write(Updated,in Comp); Zipped.CopyTo(Updated,BBACompression.Size);
        Crypt.Encrypt(Updated,Directory.EncryptionID); Directory.Length=(uint)Updated.Length; Directory.CRC32=Crc32.HashToUInt32(Updated);
        MemoryMarshal.Write(DirBytes,in Directory); Crypt.Encrypt(DirBytes,Header.EncryptionID);
        byte[] Result=new byte[Directory.Offset+Updated.Length]; Image.AsSpan(0,(int)Directory.Offset).CopyTo(Result); DirBytes.CopyTo(Result,BBAHeader.Size); Updated.CopyTo(Result,(int)Directory.Offset); File.WriteAllBytes(Path,Result);
    }

    private static async Task DocumentsTests()
    {
        await Test("custom documents root receives the bundled scripts",async ()=>
        {
            string Old=UserScriptHandler.Instance.GlobalDocuments; string Docs=Path.Combine(Root,"relocated-documents"); string Profile=Path.Combine(Docs,"THE SETTLERS - Rise of an Empire");
            Directory.CreateDirectory(Path.Combine(Profile,"Config")); File.WriteAllText(Path.Combine(Profile,"Config","Options.ini"),"[S6Patcher]\r\n");
            try
            {
                UserScriptHandler.Instance.UseDocumentsRoot(Docs); await UserScriptHandler.Instance.CreateUserScriptFiles(false);
                Equal(Docs,UserScriptHandler.Instance.GlobalDocuments); Bytes(S6Patcher.Properties.Resources.UserScriptLocal,File.ReadAllBytes(Path.Combine(Profile,"Script","UserScriptLocal.lua")));
            }
            finally { UserScriptHandler.Instance.GlobalDocuments=Old; }
        });
        Test("invalid custom documents root preserves previous routing",()=>
        { string Old=UserScriptHandler.Instance.GlobalDocuments; Throws<InvalidDataException>(()=>UserScriptHandler.Instance.UseDocumentsRoot(Root)); Equal(Old,UserScriptHandler.Instance.GlobalDocuments); });
    }
}
