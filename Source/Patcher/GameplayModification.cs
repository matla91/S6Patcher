using S6Patcher.Source.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace S6Patcher.Source.Patcher
{
    public static class GameplayModification
    {
        private sealed record XmlModification(string Archive, string Path, string[] Tags, string Value,
            int ExpectedOccurrences, bool AllowAdditionalOccurrences = false, bool SingleContainer = false);

        public static readonly List<FileDataEntry> ModifiableFileData = [];
        private static readonly List<XmlModification> XmlModifications = [];

        public static void ClearModifiableFileData()
        {
            ModifiableFileData.Clear();
            XmlModifications.Clear();
        }

        public static byte[] UpdateFileContent(FileDataEntry Entry, byte[] FileContent)
        {
            ArgumentNullException.ThrowIfNull(Entry);
            ArgumentNullException.ThrowIfNull(FileContent);
            using MemoryStream Stream = new(FileContent, writable: false);
            try
            {
                XmlDocument Document = LoadXmlDocument(Stream);
                return UpdateTags(Document, Entry);
            }
            catch (XmlException Exception)
            {
                throw XmlFailure(Entry, "The document is malformed or uses a forbidden DTD.", Exception);
            }
        }

        private static InvalidDataException XmlFailure(FileDataEntry Entry, string Message, Exception InnerException = null) =>
            new($"Cannot update XML '{Entry.BBArchiveName}:{Entry.FilePath}': {Message}", InnerException);

        private static byte[] UpdateTags(XmlDocument Document, FileDataEntry Entry)
        {
            if (Entry.Data == null || Entry.Data.Count == 0)
                throw XmlFailure(Entry, "No XML modifications were supplied.");

            List<XmlModification> Modifications = [];
            foreach (uint Index in Entry.Data.Keys.OrderBy(Index => Index))
            {
                if (Index >= XmlModifications.Count)
                    throw XmlFailure(Entry, $"Unknown XML modification index {Index}.");
                XmlModification Modification = XmlModifications[(int)Index];
                if (Modification.Archive != Entry.BBArchiveName || Modification.Path != Entry.FilePath)
                    throw XmlFailure(Entry, $"XML modification index {Index} belongs to another file.");
                Modifications.Add(Modification);
            }
            int ExpectedModifications = XmlModifications.Count(Modification =>
                Modification.Archive == Entry.BBArchiveName && Modification.Path == Entry.FilePath);
            if (Modifications.Count != ExpectedModifications)
                throw XmlFailure(Entry, $"The file requires all {ExpectedModifications} registered modifications; found {Modifications.Count}.");

            // Resolve and validate every target against the original document before changing any node.
            List<(XmlNode Node, string Value)> Updates = [];
            foreach (var Group in Modifications.GroupBy(Modification => string.Join('\0', Modification.Tags)))
            {
                XmlModification Contract = Group.First();
                string TagPath = string.Join('/', Contract.Tags);
                if (Group.Count() != Contract.ExpectedOccurrences)
                    throw XmlFailure(Entry, $"'{TagPath}' requires {Contract.ExpectedOccurrences} modifications; found {Group.Count()}.");

                List<XmlNode> Nodes = Document.GetElementsByTagName(Contract.Tags[0]).Cast<XmlNode>()
                    .Where(Node => Node.NamespaceURI == Document.DocumentElement.NamespaceURI).ToList();
                if (Contract.SingleContainer && Nodes.Count != 1)
                    throw XmlFailure(Entry, $"'{Contract.Tags[0]}' requires one container; found {Nodes.Count}.");

                if (Contract.Tags.Length > 1 && !Contract.SingleContainer && Nodes.Count > 0)
                {
                    // The documented player colors are successive Color records in one collection.
                    // Separate collections must not be flattened into one ordinal sequence.
                    XmlNode Parent = Nodes[0].ParentNode;
                    if (Nodes.Any(Node => !ReferenceEquals(Node.ParentNode, Parent)))
                        throw XmlFailure(Entry, $"'{Contract.Tags[0]}' occurs in multiple collections.");
                }

                for (int TagIndex = 1; TagIndex < Contract.Tags.Length; TagIndex++)
                {
                    List<XmlNode> ChildNodes = [];
                    foreach (XmlNode Node in Nodes)
                    {
                        List<XmlNode> Matching = Node.ChildNodes.Cast<XmlNode>().Where(Child =>
                            Child.NodeType == XmlNodeType.Element && Child.NamespaceURI == Node.NamespaceURI && Child.Name == Contract.Tags[TagIndex]).ToList();
                        if (!Contract.SingleContainer && Matching.Count != 1)
                            throw XmlFailure(Entry, $"Each '{Contract.Tags[0]}' record requires one direct '{Contract.Tags[TagIndex]}' element; found {Matching.Count}.");
                        ChildNodes.AddRange(Matching);
                    }
                    Nodes = ChildNodes;
                }

                if (Nodes.Count < Contract.ExpectedOccurrences || (!Contract.AllowAdditionalOccurrences && Nodes.Count != Contract.ExpectedOccurrences))
                    throw XmlFailure(Entry, $"'{TagPath}' requires {(Contract.AllowAdditionalOccurrences ? "at least " : "")}{Contract.ExpectedOccurrences} elements; found {Nodes.Count}.");

                int Occurrence = 0;
                foreach (XmlModification Modification in Group)
                {
                    XmlNode Node = Nodes[Occurrence++];
                    if (Node.ChildNodes.Cast<XmlNode>().Any(Child => Child.NodeType == XmlNodeType.Element))
                        throw XmlFailure(Entry, $"'{TagPath}' occurrence {Occurrence} must be a scalar value, not a container.");
                    Updates.Add((Node, Modification.Value));
                }
            }

            foreach (var Update in Updates) Update.Node.InnerText = Update.Value;
            return SerializeXmlDocument(Document);
        }

        private static XmlDocument LoadXmlDocument(Stream Stream)
        {
            XmlReaderSettings Settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            XmlDocument Document = new()
            {
                PreserveWhitespace = true
            };

            using XmlReader Reader = XmlReader.Create(Stream, Settings);
            Document.Load(Reader);
            return Document;
        }

        private static byte[] SerializeXmlDocument(XmlDocument Document)
        {
            using MemoryStream Stream = new();
            XmlWriterSettings Settings = new()
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false
            };

            using (XmlWriter Writer = XmlWriter.Create(Stream, Settings))
            {
                Document.Save(Writer);
            }

            return Stream.ToArray();
        }

        private static FileDataEntry GetModifiableFileData(string Archive, string Path)
        {
            foreach (FileDataEntry Entry in ModifiableFileData)
            {
                if (Entry.BBArchiveName == Archive && Entry.FilePath == Path)
                {
                    return Entry;
                }
            }

            return null;
        }

        private static void AddModifiableFileData(string Archive, string Path, string[] XMLTags, string Value,
            int ExpectedOccurrences, bool AllowAdditionalOccurrences = false, bool SingleContainer = false)
        {
            FileDataEntry Entry = GetModifiableFileData(Archive, Path);
            if (Entry == null)
            {
                Entry = new()
                {
                    IsDataFile = false,
                    BBArchiveName = Archive,
                    FilePath = Path,

                    OriginalFileCRC = 0x0, // Ignored when !IsDataFile
                    EntryCount = 0, // Ignored when !IsDataFile
                    Data = []
                };

                ModifiableFileData.Add(Entry);
            }

            uint Index = (uint)XmlModifications.Count;
            XmlModifications.Add(new(Archive, Path, XMLTags, Value, ExpectedOccurrences, AllowAdditionalOccurrences, SingleContainer));
            Entry.Data.Add(Index, []);
        }

        public static void ModifyPlayerColor(Avalonia.Media.Color Color)
        {
            // First file entry is always zero
            AddModifiableFileData("shrgcfg0.bba", "config\\playercolor.xml", ["Color", "Red"], "0", 2, AllowAdditionalOccurrences: true);
            AddModifiableFileData("shrgcfg0.bba", "config\\playercolor.xml", ["Color", "Green"], "0", 2, AllowAdditionalOccurrences: true);
            AddModifiableFileData("shrgcfg0.bba", "config\\playercolor.xml", ["Color", "Blue"], "0", 2, AllowAdditionalOccurrences: true);

            // Custom player color
            AddModifiableFileData("shrgcfg0.bba", "config\\playercolor.xml", ["Color", "Red"], Color.R.ToString(), 2, AllowAdditionalOccurrences: true);
            AddModifiableFileData("shrgcfg0.bba", "config\\playercolor.xml", ["Color", "Green"], Color.G.ToString(), 2, AllowAdditionalOccurrences: true);
            AddModifiableFileData("shrgcfg0.bba", "config\\playercolor.xml", ["Color", "Blue"], Color.B.ToString(), 2, AllowAdditionalOccurrences: true);
        }

        private static void RequireFourLimits(uint[] Values)
        {
            ArgumentNullException.ThrowIfNull(Values);
            if (Values.Length != 4)
                throw new ArgumentException("Exactly four upgrade limits are required.", nameof(Values));
        }

        public static void ModifySettlerLimits(uint[] Values)
        {
            RequireFourLimits(Values);
            // Preserve the existing ordinal contract: no cathedral, then four upgrade levels.
            AddModifiableFileData("shrgcfg1.bba", "config\\logic.xml", ["SettlerLimit"], "50", 5); // No cathedral
            foreach (uint Value in Values)
            {
                AddModifiableFileData("shrgcfg1.bba", "config\\logic.xml", ["SettlerLimit"], Value.ToString(), 5);
            }
        }

        public static void ModifySoldierLimits(uint[] Values)
        {
            RequireFourLimits(Values);
            string BasePath = "config\\entities\\b_castle_";
            foreach (string DefinitionFile in new string[] {"me.xml", "na.xml", "ne.xml", "se.xml"})
            {
                string FullPath = BasePath + DefinitionFile;
                foreach (uint Value in Values)
                {
                    AddModifiableFileData("shrgcfg0.bba", FullPath, ["SoldierLimits", "Limit"], Value.ToString(), 4, SingleContainer: true);
                }
            }

            foreach (uint Value in Values)
            {
                AddModifiableFileData("shrgcfge10.bba", BasePath + "as.xml", ["SoldierLimits", "Limit"], Value.ToString(), 4, SingleContainer: true);
            }
        }

        public static void ModifyStorehouseLimits(uint[] Values)
        {
            RequireFourLimits(Values);
            string StorePath = "config\\entities\\b_storehouse.xml";
            foreach (uint Value in Values)
            {
                AddModifiableFileData("shrgcfge10.bba", StorePath, ["OutStockCapacities", "OutStockCapacity"], Value.ToString(), 4, SingleContainer: true);
            }

            // The following is necessary for the extra1 storehouse to be loaded in the base game, otherwise the game crashes
            // Existing compatibility workaround targets only the first Worth; later goods are preserved.
            AddModifiableFileData("shrgcfge10.bba", "config\\goodsex.xml", ["Worth"], "0", 1, AllowAdditionalOccurrences: true);
        }
    }
}
