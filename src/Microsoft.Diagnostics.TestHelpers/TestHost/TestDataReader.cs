using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestDataReader
    {
        /// <summary>
        /// Test data value
        /// </summary>
        public class Value
        {
            private readonly object _value;

            internal Value(string valueString)
            {
                _value = valueString;
            }

            internal Value(ImmutableArray<ImmutableDictionary<string, Value>> values)
            {
                _value = values;
            }

            /// <summary>
            /// Returns true if sub values
            /// </summary>
            public bool IsSubValue => _value is ImmutableArray<ImmutableDictionary<string, Value>>;

            /// <summary>
            /// Return the sub values for nested test data
            /// </summary>
            public ImmutableArray<ImmutableDictionary<string, Value>> Values
            {
                get { return _value is ImmutableArray<ImmutableDictionary<string, Value>> values ? values : ImmutableArray<ImmutableDictionary<string, Value>>.Empty; }
            }

            /// <summary>
            /// Get the test data value as type T
            /// </summary>
            public T GetValue<T>()
            {
                return (T)GetValue(typeof(T));
            }

            /// <summary>
            /// Get the test data value as "type"
            /// </summary>
            public object GetValue(Type type)
            {
                object value = _value;
                if (value is string valueString)
                {
                    GetValue(type, valueString, ref value);
                }
                return value;
            }

            /// <summary>
            /// Convert test data string to value
            /// </summary>
            /// <param name="type">type to convert to</param>
            /// <param name="valueString">test data string</param>
            /// <param name="result">resulting object</param>
            public static void GetValue(Type type, string valueString, ref object result)
            {
                valueString = valueString.Trim();
                if (type == typeof(string))
                {
                    result = valueString ?? "";
                }
                else if (type == typeof(bool))
                {
                    switch (valueString.ToLowerInvariant())
                    {
                        case "true":
                            result = true;
                            break;
                        case "false":
                            result = false;
                            break;
                    }
                }
                else if (type.IsEnum)
                {
                    result = Enum.Parse(type, valueString);
                }
                else if (type.IsPrimitive)
                {
                    NumberStyles style = valueString.StartsWith("0x") ? NumberStyles.HexNumber : NumberStyles.Integer;
                    if (ulong.TryParse(valueString.Replace("0x", ""), style, CultureInfo.InvariantCulture, out ulong integerValue))
                    {
                        result = Convert.ChangeType(integerValue, type);
                    }
                }
            }
        }

        /// <summary>
        /// Test data file version
        /// </summary>
        public readonly Version Version;

        /// <summary>
        /// Target test data
        /// </summary>
        public readonly ImmutableDictionary<string, Value> Target;

        /// <summary>
        /// Shortcut to the module test data
        /// </summary>
        public readonly ImmutableArray<ImmutableDictionary<string, Value>> Modules;

        /// <summary>
        /// Shortcut  to the thread test data
        /// </summary>
        public readonly ImmutableArray<ImmutableDictionary<string, Value>> Threads;

        /// <summary>
        /// Shortcut to the runtime test data
        /// </summary>
        public readonly ImmutableArray<ImmutableDictionary<string, Value>> Runtimes;

        /// <summary>
        /// Open the test data xml file
        /// </summary>
        /// <param name="testDataFile"></param>
        public TestDataReader(string testDataFile)
        {
            XDocument doc = XDocument.Load(testDataFile);
            XElement root = doc.Root;
            Assert.Equal("TestData", root.Name);
            foreach (XElement child in root.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Version":
                        Version = Version.Parse(child.Value);
                        break;
                    case "Target":
                        Target = Build(child);
                        break;
                }
            }
            Modules = Target["Modules"].Values;
            Threads = Target["Threads"].Values;
            Runtimes = Target["Runtimes"].Values;
        }

        private static ImmutableDictionary<string, Value> Build(XElement node)
        {
            var members = new Dictionary<string, Value>();
            foreach (XElement dataNode in node.Elements())
            {
                string name = dataNode.Name.LocalName;
                if (dataNode.HasElements)
                {
                    var items = new List<ImmutableDictionary<string, Value>>();
                    foreach (XElement subValue in dataNode.Elements())
                    {
                        if (subValue.HasElements)
                        {
                            // Has multiple elements (i.e. Modules, Threads, Runtimes, 
                            // etc). Assumes the same name for each entry.
                            items.Add(Build(subValue));
                        }
                        else
                        {
                            // Only has sub members (i.e. RuntimeModule, etc.)
                            items.Add(Build(dataNode));
                            break;
                        }
                    }
                    members.Add(name, new Value(items.ToImmutableArray()));
                }
                else
                {
                    members.Add(name, new Value(dataNode.Value));
                }
            }
            return members.ToImmutableDictionary();
        }
    }

    public static class TestDataExtensions
    {
        /// <summary>
        /// Helper function to get a test data value 
        /// </summary>
        /// <typeparam name="T">type to convert test data value</typeparam>
        /// <param name="values">values collection to lookup name</param>
        /// <param name="name">value name</param>
        /// <param name="value">result value of type T</param>
        /// <returns></returns>
        public static bool TryGetValue<T>(
            this ImmutableDictionary<string, TestDataReader.Value> values, string name, out T value)
        {
            if (values.TryGetValue(name, out TestDataReader.Value testValue))
            {
                value = testValue.GetValue<T>();
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Finds the match item (i.e. IModule, IThread, etc.) in the test data.
        /// </summary>
        /// <typeparam name="T">field or property type</typeparam>
        /// <param name="items">Modules, Threads, Registers, etc. test data</param>
        /// <param name="propety">name of property to use for search</param>
        /// <param name="propertyValue"></param>
        /// <returns>test data values</returns>
        public static ImmutableDictionary<string, TestDataReader.Value> Find<T>(
            this ImmutableArray<ImmutableDictionary<string, TestDataReader.Value>> items, string propety, T propertyValue)
            where T : IComparable
        {
            foreach (var item in items)
            {
                TestDataReader.Value value = item[propety];
                if (propertyValue.CompareTo(value.GetValue<T>()) == 0)
                {
                    return item;
                }
            }
            return default;
        }

        /// <summary>
        /// Compares the test data values with the properties in the instance with the same name. This is
        /// used to compare ITarget, IModule, IThread, RegiserInfo instances to the test data.
        /// </summary>
        /// <param name="values">test data for the item</param>
        /// <param name="instance">object to compare</param>
        public static void CompareMembers(
            this ImmutableDictionary<string, TestDataReader.Value> values, object instance)
        {
            foreach (KeyValuePair<string, TestDataReader.Value> testData in values)
            {
                MemberInfo[] members = instance.GetType().GetMember(
                    testData.Key,
                    MemberTypes.Field | MemberTypes.Property | MemberTypes.Method,
                    BindingFlags.Public | BindingFlags.Instance);

                if (members.Length > 0)
                {
                    MemberInfo member = members.Single();
                    object memberValue = null;
                    Type memberType = null;

                    switch (member.MemberType)
                    {
                        case MemberTypes.Property:
                            memberValue = ((PropertyInfo)member).GetValue(instance);
                            memberType = ((PropertyInfo)member).PropertyType;
                            break;
                        case MemberTypes.Field:
                            memberValue = ((FieldInfo)member).GetValue(instance);
                            memberType = ((FieldInfo)member).FieldType;
                            break;
                        case MemberTypes.Method:
                            if (((MethodInfo)member).GetParameters().Length == 0)
                            {
                                memberValue = ((MethodInfo)member).Invoke(instance, null);
                                memberType = ((MethodInfo)member).ReturnType;
                            }
                            break;
                    }
                    if (memberType != null)
                    {
                        if (testData.Value.IsSubValue)
                        {
                            Trace.TraceInformation($"CompareMembers {testData.Key} sub value:");
                            CompareMembers(testData.Value.Values.Single(), memberValue);
                        }
                        else
                        {
                            Type nullableType = Nullable.GetUnderlyingType(memberType);
                            memberType = nullableType ?? memberType;

                            if (nullableType != null && memberValue == null)
                            {
                                memberValue = string.Empty;
                            }
                            else if (memberType == typeof(string))
                            {
                                memberValue ??= string.Empty;
                            }
                            else if (memberValue is ImmutableArray<byte> buildId)
                            {
                                memberType = typeof(string);
                                memberValue = !buildId.IsDefaultOrEmpty ? string.Concat(buildId.Select((b) => b.ToString("x2"))) : string.Empty;
                            }
                            else if (!memberType.IsPrimitive && !memberType.IsEnum)
                            {
                                memberType = typeof(string);
                                memberValue = memberValue?.ToString() ?? string.Empty;
                            }
                            object testDataValue = testData.Value.GetValue(memberType);
                            Trace.TraceInformation($"CompareMembers {testData.Key}: expected '{testDataValue}' == actual '{memberValue}'");
                            Assert.Equal(testDataValue, memberValue);
                        }
                    }
                    else 
                    { 
                        Trace.TraceWarning($"CompareMembers {testData.Key} member not found");
                        return;
                    }
                }
                else
                {
                    Trace.TraceWarning($"CompareMembers {testData.Key} not found");
                }
            }
        }
    }
}
