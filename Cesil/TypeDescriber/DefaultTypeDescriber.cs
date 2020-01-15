﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Microsoft.CSharp.RuntimeBinder;

namespace Cesil
{
    /// <summary>
    /// The default implementation of ITypeDescriber used to
    ///   determine how to (de)serialize types and how to convert
    ///   dynamic cells and rows.
    ///   
    /// It will serialize all public properties, any fields
    ///   with a [DataMember], and will respect ShouldSerialize()
    ///   methods.
    ///   
    /// It will deserialize all public properties, any fields
    ///   with a [DataMember], and will call Reset() methods.  Expects
    ///   a public parameterless constructor for any deserialized types.
    /// 
    /// It will convert cells to most built-in types, and map rows to
    ///   POCOs, ValueTuples, Tuples, and IEnumerables.
    /// 
    /// This type is unsealed to allow for easy extension of it's behavior.
    /// </summary>
    [IntentionallyExtensible("Does 'what is expected' so minor tweaks can be handled with inheritance.")]
    public class DefaultTypeDescriber : ITypeDescriber
    {
        /// <summary>
        /// Construct a new DefaultTypeDesciber.
        /// 
        /// A pre-allocated instance is available on TypeDescribers.Default.
        /// </summary>
        public DefaultTypeDescriber() { }

        /// <summary>
        /// Gets an InstanceProvider that wraps the parameterless constructor
        ///   for the given type.
        ///   
        /// Throws if there is no parameterless constructor.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        public virtual InstanceProvider? GetInstanceProvider(TypeInfo forType)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));

            var cons = forType.GetConstructor(TypeInfo.EmptyTypes);
            if (cons == null)
            {
                return Throw.ArgumentException<InstanceProvider>($"No parameterless constructor found for {forType}", nameof(forType));
            }

            return InstanceProvider.ForParameterlessConstructor(cons);
        }

        /// <summary>
        /// Enumerate all columns to deserialize.
        /// </summary>
        public virtual IEnumerable<DeserializableMember> EnumerateMembersToDeserialize(TypeInfo forType)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));

            var buffer = new List<(DeserializableMember Member, int? Position)>();

            foreach (var p in forType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                if (!ShouldDeserialize(forType, p)) continue;

                var name = GetDeserializationName(forType, p);
                var setter = GetSetter(forType, p);
                var parser = GetParser(forType, p);
                var order = GetOrder(forType, p);
                var isRequired = GetIsRequired(forType, p);
                var reset = GetReset(forType, p);

                buffer.Add((DeserializableMember.CreateInner(forType, name, setter, parser, isRequired, reset), order));
            }

            foreach (var f in forType.GetFields())
            {
                if (!ShouldDeserialize(forType, f)) continue;

                var name = GetDeserializationName(forType, f);
                var setter = GetSetter(forType, f);
                var parser = GetParser(forType, f);
                var order = GetOrder(forType, f);
                var isRequired = GetIsRequired(forType, f);
                var reset = GetReset(forType, f);

                buffer.Add((DeserializableMember.CreateInner(forType, name, setter, parser, isRequired, reset), order));
            }

            buffer.Sort(TypeDescribers.DeserializableComparer);

            return Map(buffer);

            static IEnumerable<DeserializableMember> Map(List<(DeserializableMember Member, int? Position)> ret)
            {
                foreach (var (member, _) in ret)
                {
                    yield return member;
                }
            }
        }

        // property deserialization defaults

        /// <summary>
        /// Returns true if the given property should be deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual bool ShouldDeserialize(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            if (property.SetMethod == null) return false;

            var ignoreDataMember = property.GetCustomAttribute<IgnoreDataMemberAttribute>();
            if (ignoreDataMember != null)
            {
                return false;
            }

            var dataMember = property.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                return true;
            }

            return
                property.SetMethod != null &&
                property.SetMethod.IsPublic &&
                !property.SetMethod.IsStatic &&
                property.SetMethod.GetParameters().Length == 1 &&
                Parser.GetDefault(property.SetMethod.GetParameters()[0].ParameterType.GetTypeInfo()) != null;
        }

        /// <summary>
        /// Returns the name of the column that should map to the given property when deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual string GetDeserializationName(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return GetName(property);
        }

        /// <summary>
        /// Returns the setter to use for the given property when deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Setter? GetSetter(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return (Setter?)property.SetMethod;
        }

        /// <summary>
        /// Returns the parser to use for the column that maps to the given property when deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Parser? GetParser(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            var set = property.SetMethod;
            if (set == null)
            {
                return Throw.ArgumentException<Parser?>("Property has no setter", nameof(property));
            }

            var ps = set.GetParameters();
            if (ps.Length != 1)
            {
                return Throw.ArgumentException<Parser?>($"Setter takes {ps.Length} parameters, but must take 1", nameof(property));
            }

            var p = ps[0];
            var propertyType = p.ParameterType.GetTypeInfo();

            return GetParser(propertyType);
        }

        /// <summary>
        /// Returns the index of the column that should map to the given property.  Headers
        ///   can change this during deserialization.
        ///   
        /// Return null to leave order unspecified.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual int? GetOrder(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return GetOrder(property);
        }

        /// <summary>
        /// Returns whether or not the given property is required during deserialization.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual MemberRequired GetIsRequired(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return GetIsRequired(property);
        }

        /// <summary>
        /// Returns the reset method, if any, to call prior to deserializing the given property.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Reset? GetReset(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            // intentionally letting this be null
            var mtd = forType.GetMethod("Reset" + property.Name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (mtd == null) return null;

            if (mtd.IsStatic)
            {
                if (mtd.GetParameters().Length > 1) return null;
            }
            else
            {
                if (mtd.GetParameters().Length != 0) return null;
            }

            return Reset.ForMethod(mtd);
        }

        // field deserialization defaults


        /// <summary>
        /// Returns true if the given field should be deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual bool ShouldDeserialize(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            var dataMember = field.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the name of the column that should map to the given field when deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual string GetDeserializationName(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetName(field);
        }

        /// <summary>
        /// Returns the setter to use for the given field when deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Setter? GetSetter(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return Setter.ForField(field);
        }

        /// <summary>
        /// Returns the parser to use for the column that maps to the given property when deserialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Parser? GetParser(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetParser(field.FieldType.GetTypeInfo());
        }

        /// <summary>
        /// Returns the index of the column that should map to the given field.  Headers
        ///   can change this during deserialization.
        ///   
        /// Return null to leave order unspecified.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual int? GetOrder(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetOrder(field);
        }

        /// <summary>
        /// Returns whether or not the given field is required during deserialization.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual MemberRequired GetIsRequired(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetIsRequired(field);
        }

        /// <summary>
        /// Returns the reset method, if any, to call prior to deserializing the given field.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Reset? GetReset(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return null;
        }

        // common deserialization defaults

        private static string GetName(MemberInfo member)
        {
            var dataMember = member.GetCustomAttribute<DataMemberAttribute>();
            if (!string.IsNullOrWhiteSpace(dataMember?.Name))
            {
                return dataMember.Name;
            }

            return member.Name;
        }

        private static Parser? GetParser(TypeInfo forType)
        => Parser.GetDefault(forType);

        private static MemberRequired GetIsRequired(MemberInfo member)
        {
            var dataMember = member.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                return dataMember.IsRequired ? MemberRequired.Yes : MemberRequired.No;
            }

            return MemberRequired.No;
        }

        /// <summary>
        /// Enumerate all columns to deserialize.
        /// </summary>
        public virtual IEnumerable<SerializableMember> EnumerateMembersToSerialize(TypeInfo forType)
        {
            var buffer = new List<(SerializableMember Member, int? Position)>();

            foreach (var p in forType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                if (!ShouldSerialize(forType, p)) continue;

                var name = GetSerializationName(forType, p);
                var getter = GetGetter(forType, p);
                var shouldSerialize = GetShouldSerialize(forType, p);
                var formatter = GetFormatter(forType, p);
                var order = GetOrder(forType, p);
                var emitDefault = GetEmitDefaultValue(forType, p);

                buffer.Add((SerializableMember.CreateInner(forType, name, getter, formatter, shouldSerialize, emitDefault), order));
            }

            foreach (var f in forType.GetFields())
            {
                if (!ShouldSerialize(forType, f)) continue;

                var name = GetSerializationName(forType, f);
                var getter = GetGetter(forType, f);
                var shouldSerialize = GetShouldSerialize(forType, f);
                var formatter = GetFormatter(forType, f);
                var order = GetOrder(forType, f);
                var emitDefault = GetEmitDefaultValue(forType, f);

                buffer.Add((SerializableMember.CreateInner(forType, name, getter, formatter, shouldSerialize, emitDefault), order));
            }

            buffer.Sort(TypeDescribers.SerializableComparer);

            foreach (var (member, _) in buffer)
            {
                yield return member;
            }
        }

        // property serialization defaults


        /// <summary>
        /// Returns true if the given property should be serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual bool ShouldSerialize(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            if (property.GetMethod == null) return false;

            var ignoreDataMember = property.GetCustomAttribute<IgnoreDataMemberAttribute>();
            if (ignoreDataMember != null)
            {
                return false;
            }

            var dataMember = property.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                return true;
            }

            return
                property.GetMethod.IsPublic &&
                !property.GetMethod.IsStatic &&
                property.GetMethod.GetParameters().Length == 0 &&
                property.GetMethod.ReturnType != Types.VoidType &&
                Formatter.GetDefault(property.GetMethod.ReturnType.GetTypeInfo()) != null;
        }

        /// <summary>
        /// Returns the name of the column that should map to the given property when serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual string GetSerializationName(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return GetName(property);
        }

        /// <summary>
        /// Returns the getter to use for the given property when serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Getter? GetGetter(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return (Getter?)property.GetMethod;
        }

        /// <summary>
        /// Returns the ShouldSerializeXXX()-style method to use for the given property when serializing, if
        ///   any.
        ///  
        /// If specified, the method will be invoked for each record to determine whether to write
        ///   the property.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual ShouldSerialize? GetShouldSerialize(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            // intentionally letting this be null
            var mtd = forType.GetMethod("ShouldSerialize" + property.Name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (mtd == null) return null;

            if (mtd.ReturnType != Types.BoolType) return null;

            if (mtd.IsStatic)
            {
                if (mtd.GetParameters().Length > 1) return null;
            }
            else
            {
                if (mtd.GetParameters().Length != 0) return null;
            }

            return (ShouldSerialize?)mtd;
        }

        /// <summary>
        /// Returns the formatter to use for the column that maps to the given property when serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Formatter? GetFormatter(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return GetFormatter(property.PropertyType.GetTypeInfo());
        }

        /// <summary>
        /// Returns whether or not the default value should be serialized for the given property.
        /// 
        /// For reference types, the default value is `null`.  For ValueTypes the default value
        ///   is either 0 or the equivalent of initializing all of it's fields with their default
        ///   values.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual EmitDefaultValue GetEmitDefaultValue(TypeInfo forType, PropertyInfo property)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(property, nameof(property));

            return GetEmitDefaultValue(property);
        }

        // field serialization defaults

        /// <summary>
        /// Returns true if the given field should be serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual bool ShouldSerialize(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            var dataMember = field.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the getter to use for the given field when serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Getter? GetGetter(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return Getter.ForField(field);
        }

        /// <summary>
        /// Returns the name of the column that should map to the given field when serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual string GetSerializationName(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetName(field);
        }

        /// <summary>
        /// Returns the ShouldSerializeXXX()-style method to use for the given field when serializing, if
        ///   any. By default, always returns null.
        ///  
        /// If specified, the method will be invoked for each record to determine whether to write
        ///   the field.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual ShouldSerialize? GetShouldSerialize(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return null;
        }

        /// <summary>
        /// Returns the formatter to use for the column that maps to the given field when serialized.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected virtual Formatter? GetFormatter(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetFormatter(field.FieldType.GetTypeInfo());
        }

        /// <summary>
        /// Returns whether or not the default value should be serialized for the given property.
        /// 
        /// For reference types, the default value is `null`.  For ValueTypes the default value
        ///   is either 0 or the equivalent of initializing all of it's fields with their default
        ///   values.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        protected virtual EmitDefaultValue GetEmitDefaultValue(TypeInfo forType, FieldInfo field)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));
            Utils.CheckArgumentNull(field, nameof(field));

            return GetEmitDefaultValue(field);
        }

        // common serialization defaults
        private static Formatter? GetFormatter(TypeInfo t)
        => Formatter.GetDefault(t);

        private static int? GetOrder(MemberInfo member)
        {
            var dataMember = member.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                return dataMember.Order;
            }

            return null;
        }

        private static EmitDefaultValue GetEmitDefaultValue(MemberInfo member)
        {
            var dataMember = member.GetCustomAttribute<DataMemberAttribute>();
            if (dataMember != null)
            {
                if (dataMember.EmitDefaultValue)
                {
                    return EmitDefaultValue.Yes;
                }
                else
                {
                    return EmitDefaultValue.No;
                }
            }

            return EmitDefaultValue.Yes;
        }

        /// <summary>
        /// Enumerates cells on the given dynamic row.
        /// 
        /// Null rows have no cells, but are legal.
        /// 
        /// Rows created by Cesil have their cells enumerated as strings.
        /// 
        /// Other dynamic types will have each member enumerated as either their
        ///   actual type (if a formatter is available) or as a string.
        /// 
        /// Override to tweak behavior.
        /// </summary>
        public virtual IEnumerable<DynamicCellValue> GetCellsForDynamicRow(in WriteContext context, dynamic row)
        {
            // handle no value
            var rowObj = row as object;
            if (rowObj == null)
            {
                return Array.Empty<DynamicCellValue>();
            }

            // handle serializing our own dynamic types
            if (rowObj is DynamicRow asOwnRow)
            {
                var cols = asOwnRow.Columns.Value;

                var ret = new DynamicCellValue[cols.Count];

                var ix = 0;
                foreach (var col in cols)
                {
                    var name = col.Name;
                    if (!ShouldIncludeCell(name, in context, rowObj)) continue;

                    var valueRaw = asOwnRow.GetDataSpan(ix);
                    var value = new string(valueRaw);

                    var formatter = GetFormatter(Types.StringType, name, in context, rowObj);
                    if (formatter == null)
                    {
                        return Throw.InvalidOperationException<IEnumerable<DynamicCellValue>>($"No formatter returned by {nameof(GetFormatter)}");
                    }

                    ret[ix] = DynamicCellValue.Create(name, value, formatter);
                    ix++;
                }

                return ret;
            }

            // special case the most convenient dynamic type
            if (rowObj is ExpandoObject asExpando)
            {
                var ret = new List<DynamicCellValue>();

                foreach (var kv in asExpando)
                {
                    var name = kv.Key;
                    var value = kv.Value;
                    Formatter? formatter;

                    if (!ShouldIncludeCell(name, in context, rowObj)) continue;

                    if (value == null)
                    {
                        formatter = GetFormatter(Types.StringType, name, in context, rowObj);
                    }
                    else
                    {
                        var valueType = value.GetType().GetTypeInfo();
                        formatter = GetFormatter(valueType, name, in context, rowObj);
                        if (formatter == null)
                        {
                            // try and coerce into a string?
                            var convert = Microsoft.CSharp.RuntimeBinder.Binder.Convert(0, Types.StringType, valueType);
                            var convertCallSite = CallSite<Func<CallSite, object, object>>.Create(convert);
                            try
                            {
                                value = convertCallSite.Target.Invoke(convertCallSite, value);
                                formatter = Formatter.GetDefault(Types.StringType);
                            }
                            catch
                            {
                                /* intentionally left blank */
                            }
                        }
                    }

                    // skip anything that isn't formattable
                    if (formatter == null) continue;

                    ret.Add(DynamicCellValue.Create(name, value, formatter));
                }

                return ret;
            }

            var rowObjType = rowObj.GetType().GetTypeInfo();

            // now the least convenient dynamic type
            if (rowObj is IDynamicMetaObjectProvider asDynamic)
            {
                var ret = new List<DynamicCellValue>();

                var arg = Expressions.Parameter_Object;
                var metaObj = asDynamic.GetMetaObject(arg);

                var names = metaObj.GetDynamicMemberNames();
                foreach (var name in names)
                {
                    var args = new[] { CSharpArgumentInfo.Create(default, null) };
                    var getMember = Microsoft.CSharp.RuntimeBinder.Binder.GetMember(default, name, rowObjType, args);
                    var getMemberCallSite = CallSite<Func<CallSite, object, object>>.Create(getMember);

                    var skip = false;
                    object? value;
                    Formatter? formatter;
                    try
                    {
                        value = getMemberCallSite.Target.Invoke(getMemberCallSite, rowObj);
                    }
                    catch
                    {
                        value = null;
                        skip = true;
                    }

                    // skip it, access failed
                    if (skip) continue;

                    if (!ShouldIncludeCell(name, in context, rowObj)) continue;

                    if (value == null)
                    {
                        formatter = GetFormatter(Types.StringType, name, in context, rowObj);
                    }
                    else
                    {
                        var valueType = value.GetType().GetTypeInfo();
                        formatter = GetFormatter(valueType, name, in context, rowObj);

                        if (formatter == null)
                        {
                            // try and coerce into a string?
                            var convert = Microsoft.CSharp.RuntimeBinder.Binder.Convert(0, Types.StringType, valueType);
                            var convertCallSite = CallSite<Func<CallSite, object, object>>.Create(convert);
                            try
                            {
                                value = convertCallSite.Target.Invoke(convertCallSite, value);
                                formatter = GetFormatter(Types.StringType, name, in context, rowObj);
                            }
                            catch
                            {
                                /* intentionally left blank */
                            }
                        }
                    }

                    // skip it, can't serialize it
                    if (formatter == null) continue;

                    ret.Add(DynamicCellValue.Create(name, value, formatter));
                }

                return ret;
            }

            // now just plain old types
            {
                var ret = new List<DynamicCellValue>();

                var toSerialize = EnumerateMembersToSerialize(rowObjType);
                foreach (var mem in toSerialize)
                {
                    var name = mem.Name;
                    if (!ShouldIncludeCell(name, in context, rowObj)) continue;

                    var getter = mem.Getter;

                    var formatter = GetFormatter(getter.Returns, name, in context, rowObj);
                    if (formatter == null)
                    {
                        return Throw.InvalidOperationException<IEnumerable<DynamicCellValue>>($"No formatter returned by {nameof(GetFormatter)}");
                    }

                    var delProvider = ((ICreatesCacheableDelegate<Getter.DynamicGetterDelegate>)getter);
                    delProvider.Guarantee(DefaultTypeDescriberDelegateCache.Instance);
                    var value = delProvider.CachedDelegate.Value(rowObj, in context);

                    ret.Add(DynamicCellValue.Create(name, value, formatter));
                }

                return ret;
            }
        }

        /// <summary>
        /// Called in GetCellsForDynamicRow to determine whether a cell should be included.
        /// 
        /// Override to customize behavior.
        /// </summary>
        protected bool ShouldIncludeCell(string name, in WriteContext context, dynamic row)
        => true;

        /// <summary>
        /// Called in GetCellsForDynamicRow to determine the formatter that should be used for a cell.
        /// 
        /// Override to customize behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        protected Formatter? GetFormatter(TypeInfo forType, string name, in WriteContext context, dynamic row)
        => Formatter.GetDefault(forType);

        /// <summary>
        /// Returns a Parser that can be used to parse the targetType.
        /// 
        /// Override to customize behavior.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        public virtual Parser? GetDynamicCellParserFor(in ReadContext context, TypeInfo targetType)
        {
            var onePCons = targetType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Types.ParserConstructorOneParameterTypes_Array, null);
            var twoPCons = targetType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Types.ParserConstructorTwoParameterTypes_Array, null);
            var cons = onePCons ?? twoPCons;
            if (cons != null)
            {
                return Parser.ForConstructor(cons);
            }

            var parser = Parser.GetDefault(targetType);
            if (parser != null)
            {
                return parser;
            }

            return null;
        }

        /// <summary>
        /// Returns a DynamicRowConverter that can be used to parse the targetType,
        ///    if a default parser for the type exists or a constructor accepting
        ///    the appropriate number of objects (can be dynamic in source) is on 
        ///    the type.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        public virtual DynamicRowConverter? GetDynamicRowConverter(in ReadContext context, IEnumerable<ColumnIdentifier> columns, TypeInfo targetType)
        {
            // handle tuples
            if (IsValueTuple(targetType))
            {
                var mtd = Types.TupleDynamicParsersType.MakeGenericType(targetType).GetTypeInfo();
                var genMtd = mtd.GetMethodNonNull(nameof(TupleDynamicParsers<object>.TryConvertValueTuple), BindingFlags.Static | BindingFlags.NonPublic);
                return DynamicRowConverter.ForMethod(genMtd);
            }
            else if (IsTuple(targetType))
            {
                var mtd = Types.TupleDynamicParsersType.MakeGenericType(targetType).GetTypeInfo();
                var genMtd = mtd.GetMethodNonNull(nameof(TupleDynamicParsers<object>.TryConvertTuple), BindingFlags.Static | BindingFlags.NonPublic);
                return DynamicRowConverter.ForMethod(genMtd);
            }

            // handle IEnumerables
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition().GetTypeInfo() == Types.IEnumerableOfTType)
            {
                var elementType = targetType.GetGenericArguments()[0];
                var genEnum = Types.DynamicRowEnumerableType.MakeGenericType(elementType).GetTypeInfo();
                var cons = genEnum.GetConstructorNonNull(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { Types.ObjectType }, null);
                return DynamicRowConverter.ForConstructorTakingDynamic(cons);
            }
            else if (targetType == Types.IEnumerableType)
            {
                var cons = Types.DynamicRowEnumerableNonGenericType.GetConstructorNonNull(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { Types.ObjectType }, null);
                return DynamicRowConverter.ForConstructorTakingDynamic(cons);
            }

            int width;
            if(columns is ICollection<ColumnIdentifier> c)
            {
                width = c.Count;
            }
            else
            {
                width = 0;
                foreach(var _ in columns)
                {
                    width++;
                }
            }

            var isConsPOCO = IsConstructorPOCO(width, targetType);
            if (isConsPOCO.HasValue)
            {
                return DynamicRowConverter.ForConstructorTakingTypedParameters(isConsPOCO.Constructor.Value, isConsPOCO.Columns.Value);
            }

            var isPropPOCO = IsPropertyPOCO(targetType, columns);
            if (isPropPOCO.HasValue)
            {
                return DynamicRowConverter.ForEmptyConstructorAndSetters(isPropPOCO.Constructor.Value, isPropPOCO.Setters.Value, isPropPOCO.Columns.Value);
            }

            return null;
        }

        private static bool IsTuple(TypeInfo t)
        {
            if (!t.IsGenericType || t.IsGenericTypeDefinition) return false;

            var genType = t.GetGenericTypeDefinition();
            return Array.IndexOf(Types.TupleTypes, genType) != -1;
        }

        private static bool IsValueTuple(TypeInfo t)
        {
            if (!t.IsGenericType || t.IsGenericTypeDefinition) return false;

            var genType = t.GetGenericTypeDefinition();
            return Array.IndexOf(Types.ValueTupleTypes, genType) != -1;
        }

        private static ConstructorPOCOResult IsConstructorPOCO(int width, TypeInfo type)
        {
            foreach (var cons in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var consPs = cons.GetParameters();
                if (consPs.Length != width) continue;

                var selectedCons = cons;
                var columnIndexes = new ColumnIdentifier[consPs.Length];
                for (var i = 0; i < columnIndexes.Length; i++)
                {
                    columnIndexes[i] = ColumnIdentifier.Create(i);
                }

                return new ConstructorPOCOResult(cons, columnIndexes);
            }

            return ConstructorPOCOResult.Empty;
        }

        private static PropertyPOCOResult IsPropertyPOCO(TypeInfo type, IEnumerable<ColumnIdentifier> columns)
        {
            var emptyCons = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (emptyCons == null)
            {
                return PropertyPOCOResult.Empty;
            }

            var allProperties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            var setters = new Setter[allProperties.Length];
            var columnIndexes = new ColumnIdentifier[allProperties.Length];

            var ix = 0;
            var i = 0;
            foreach (var col in columns)
            {
                if (!col.HasName)
                {
                    return PropertyPOCOResult.Empty;
                }

                var colName = col.Name;

                PropertyInfo? prop = null;
                for (var j = 0; j < allProperties.Length; j++)
                {
                    var p = allProperties[j];
                    if (p.Name == colName)
                    {
                        prop = p;
                    }
                }

                if (prop == null)
                {
                    goto loopEnd;
                }

                var setterMtd = prop.SetMethod;
                if (setterMtd == null)
                {
                    goto loopEnd;
                }

                if (setterMtd.ReturnType.GetTypeInfo() != Types.VoidType)
                {
                    goto loopEnd;
                }

                if (setterMtd.GetParameters().Length != 1) continue;

                setters[ix] = Setter.ForMethod(setterMtd);
                columnIndexes[ix] = ColumnIdentifier.Create(i);

                ix++;

loopEnd:
                i++;
            }

            if (ix != setters.Length)
            {
                Array.Resize(ref setters, ix);
                Array.Resize(ref columnIndexes, ix);
            }

            return new PropertyPOCOResult(emptyCons, setters, columnIndexes);
        }

        /// <summary>
        /// Returns a representation of this DefaultTypeDescriber object.
        /// 
        /// Only for debugging, this value is not guaranteed to be stable.
        /// </summary>
        public override string ToString()
        {
            var isCommon = ReferenceEquals(this, TypeDescribers.Default);
            if (isCommon)
            {
                return $"{nameof(DefaultTypeDescriber)} Shared Instance";
            }

            var t = GetType().GetTypeInfo();

            if (t == Types.DefaultTypeDescriberType)
            {
                return $"{nameof(DefaultTypeDescriber)} Unique Instance";
            }

            return $"{nameof(DefaultTypeDescriber)} Subclass {t.Name}";
        }
    }
}
