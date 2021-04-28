using RisHelper.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RisHelper
{
    public static class RisReader
    {
        private const string _entriesSeparator = "\r\n\r\n";
        private const string _fieldsSeparator = "\n";
        private const string _fieldSeparator = "  - ";
        private static readonly ConcurrentDictionary<Type, IRisFieldResolver> _resolvers = new ConcurrentDictionary<Type, IRisFieldResolver>();
        private static readonly ConcurrentDictionary<string, RisMapping> _mappings = new ConcurrentDictionary<string, RisMapping>();

        public static IEnumerable<RisRecord> Read(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (File.Exists(path) == false)
            {
                throw new FileNotFoundException($"Invalid path {path}");
            }

            return Read(File.ReadAllBytes(path));
        }

        public static IEnumerable<RisRecord> Read(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            using (var memoryStream = new MemoryStream(bytes))
            {
                using (var streamReader = new StreamReader(memoryStream))
                {
                    var content = streamReader.ReadToEnd();
                    var entries = Regex.Split(content, _entriesSeparator);
                    var records = ReadRecords(entries);
                    return records;
                }
            }
        }

        private static IEnumerable<RisRecord> ReadRecords(string[] entries)
        {
            List<RisRecord> records = new List<RisRecord>();

            if (entries == null)
            {
                return records;
            }

            BuildMappings();

            foreach (var entry in entries)
            {
                var record = ReadRecord(entry);
                if (record != null)
                {
                    records.Add(record);
                }
            }

            return records;
        }

        private static RisRecord ReadRecord(string entry)
        {
            if (string.IsNullOrEmpty(entry))
            {
                return null;
            }

            var record = new RisRecord();
            var fields = Regex.Split(entry, _fieldsSeparator);
            foreach(var field in fields)
            {
                var fieldArray = Regex.Split(field, _fieldSeparator);
                if (fieldArray.Length == 2)
                {
                    if (_mappings.TryGetValue(fieldArray[0], out RisMapping mapping))
                    {
                        var tag = fieldArray[0];
                        var srcValue = fieldArray[1];
                        var destValue = mapping.Property.GetValue(record);
                        mapping.Property.SetValue(record, mapping.Resolver.Resolve(tag, srcValue, destValue), null);
                    }
                }
            }

            return record;
        }

        private static void BuildMappings()
        {
            var type = typeof(RisRecord);
            var properties = type.GetProperties();
            foreach(var property in properties)
            {
                var attributes = property.GetCustomAttributes(false);
                foreach(var attribute in attributes)
                {
                    if (attribute is RisFieldAttribute fieldAttribute)
                    {
                        var tags = fieldAttribute.Tag.Split(',').Select(x => x.Trim());
                        foreach(var tag in tags)
                        {
                            var resolver = _resolvers.GetOrAdd(fieldAttribute.ResolverType,
                                (t) => (IRisFieldResolver)Activator.CreateInstance(fieldAttribute.ResolverType));

                            _mappings[tag] = new RisMapping(property, resolver);
                        }
                    }
                }
            }
        }
    }
}
