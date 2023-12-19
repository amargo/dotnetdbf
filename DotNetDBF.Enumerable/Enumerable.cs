﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Runtime.CompilerServices;
using ImpromptuInterface;
using Dynamitey;
using System.Xml.Linq;

namespace DotNetDBF.Enumerable
{
    /// <summary>
    /// Interface to get the contents of the DBF Wrapper
    /// </summary>
    public interface IDBFInterceptor
    {
        /// <summary>
        /// Does field exist in row
        /// </summary>
        /// <returns></returns>
        bool Exists(string fieldName);

        /// <summary>
        /// Gets the data row.
        /// </summary>
        /// <returns></returns>
        object[] GetDataRow();

        IEnumerable<string> GetDynamicMemberNames();

        bool TryGetMember(string name, out object result);

        bool TryGetMember(GetMemberBinder binder, out object result);

        bool TrySetMember(string name, object value);

        bool TrySetMember(SetMemberBinder binder, object value);
    }

#pragma warning disable 618
    public class DBFInterceptor : DBFIntercepter
#pragma warning restore 618
    {
        public DBFInterceptor(object[] wrappedObj, string[] fieldNames) : base(wrappedObj, fieldNames)
        {
        }
    }


    /// <summary>
    /// DBF Dynamic Wrapper
    /// </summary>
    public abstract class BaseDBFInterceptor : Dynamitey.DynamicObjects.BaseObject, IDBFInterceptor
    {
        private readonly string[] _fieldNames;
        private readonly object[] _wrappedArray;

        protected BaseDBFInterceptor(object[] wrappedObj, string[] fieldNames)
        {
            _wrappedArray = wrappedObj;
            _fieldNames = fieldNames;
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _fieldNames;
        }

        public bool Exists(string fieldName)
        {
            return _fieldNames.Contains(fieldName);
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            return TryGetMember(binder.Name, out result);
        }

        public bool TryGetMember(string name, out object result)
        {
            result = null;
            var tLookup = name;
            var tIndex = Array.FindIndex(_fieldNames,
                it => it.Equals(tLookup, StringComparison.InvariantCultureIgnoreCase));

            if (tIndex < 0)
                return false;


            result = _wrappedArray[tIndex];


            if (TryTypeForName(tLookup, out var outType))
            {
                result = Dynamic.CoerceConvert(result, outType);
            }

            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            return TrySetMember(binder.Name, value);
        }

        public bool TrySetMember(string name, object value)
        {
            var tLookup = name;
            var tIndex = Array.FindIndex(_fieldNames,
                it => it.Equals(tLookup, StringComparison.InvariantCultureIgnoreCase));

            if (tIndex < 0)
                return false;

            if (TryTypeForName(tLookup, out var outType))
            {
                value = Dynamic.CoerceConvert(value, outType);
            }

            _wrappedArray[tIndex] = value;

            return true;
        }

        public object[] GetDataRow()
        {
            return _wrappedArray;
        }
    }

    /// <summary>
    /// Enumerable API
    /// </summary>
    public static partial class DBFEnumerable
    {
        /// <summary>
        /// New Blank Row Dynamic object that matches writer;
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <returns></returns>
        public static dynamic NewBlankRow(this DBFWriter writer)
        {
            var fields = writer.Fields.Select(it => it.Name).ToArray();
            var obj = new object[fields.Length];
            return new Enumerable.DBFInterceptor(obj, fields);
        }

        public static void CopyRecordTo(this IDBFInterceptor original, IDBFInterceptor dest)
        {
            foreach (var fieldName in Dynamitey.Dynamic.GetMemberNames(dest, true))
            {
                try
                {
                    var val = Dynamic.InvokeGet(original, fieldName);
                    Dynamic.InvokeSet(dest, fieldName, val);
                }
                catch
                {
                    // ignored
                }
            }
        }


        /// <summary>
        /// Writes the record.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The value.</param>
        public static void WriteInterceptorRecord(this DBFWriter writer, Enumerable.IDBFInterceptor value)
        {
            writer.WriteRecord(value.GetDataRow());
        }

        /// <summary>
        /// Adds the record.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The value.</param>
        public static void AddInterceptorRecord(this DBFWriter writer, Enumerable.IDBFInterceptor value)
        {
            writer.AddRecord(value.GetDataRow());
        }

        /// <summary>
        /// Return all the records. T should be interface with getter properties that match types and names of the database. 
        /// Optionally instead of T being and interface you can pass in an anonymous object with properties that match that 
        /// database and then you'll get an IEnumerable of that anonymous type with the data filled in.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader">The reader.</param>
        /// <param name="prototype">The prototype. Anonymous class instance</param>
        /// <returns></returns>
        public static IEnumerable<T> AllRecords<T>(this DBFReader reader, T prototype = null) where T : class
        {
            var tType = typeof(T);

            var tProperties = tType.GetProperties()
                .Where(
                    it =>
                        Array.FindIndex(reader.Fields,
                            f => f.Name.Equals(it.Name, StringComparison.InvariantCultureIgnoreCase)) >= 0)
                .ToList();
            var tProps = tProperties
                .Select(
                    it =>
                        Array.FindIndex(reader.Fields,
                            jt => jt.Name.Equals(it.Name, StringComparison.InvariantCultureIgnoreCase)))
                .Where(it => it >= 0)
                .ToArray();

            var tOrderedProps = tProps.OrderBy(it => it).ToArray();
            var tReturn = new List<T>();


            if (tType.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Any())
            {
                var tAnon = reader.NextRecord(tProps, tOrderedProps);
                while (tAnon != null)
                {
                    tReturn.Add((T) Activator.CreateInstance(tType, tAnon));
                    tAnon = reader.NextRecord(tProps, tOrderedProps);
                }


                return tReturn;
            }

            var t = reader.NextRecord(tProps, tOrderedProps);

            while (t != null)
            {
                var interceptor = new Enumerable.DBFInterceptor(t, tProperties.Select(it => it.Name).ToArray());

                tReturn.Add(interceptor.ActLike<T>(typeof(Enumerable.IDBFInterceptor)));
                t = reader.NextRecord(tProps, tOrderedProps);
            }


            return tReturn;
        }

        /// <summary>
        /// Returns a list of dynamic objects whose properties and types match up with that database name.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="whereColumn">The where column name.</param>
        /// <param name="whereColumnEquals">What the were column should equal.</param>
        /// <returns></returns>
        public static IEnumerable<dynamic> DynamicAllRecords(this DBFReader reader, string whereColumn = null,
            dynamic whereColumnEquals = null)
        {
            var props = reader.GetSelectFields().Select(it => it.Name).ToArray();

            int? whereColumnIndex = null;
            if (!String.IsNullOrEmpty(whereColumn))
            {
                whereColumnIndex = Array.FindIndex(props,
                    it => it.Equals(whereColumn, StringComparison.InvariantCultureIgnoreCase));
            }


            var tReturn = new List<object>();
            var t = reader.NextRecord();

            while (t != null)
            {
                if (whereColumnIndex is int i)
                {
                    dynamic tO = t[i];
                    if (!tO.Equals(whereColumnEquals))
                    {
                        t = reader.NextRecord();
                        continue;
                    }
                }


                var interceptor = new Enumerable.DBFInterceptor(t, props);


                tReturn.Add(interceptor);
                t = reader.NextRecord();
            }


            return tReturn;
        }
    }
}