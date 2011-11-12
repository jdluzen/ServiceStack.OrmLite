//
// ServiceStack.OrmLite: Light-weight POCO ORM for .NET and Mono
//
// Authors:
//   Demis Bellot (demis.bellot@gmail.com)
//
// Copyright 2010 Liquidbit Ltd.
//
// Licensed under the same terms of ServiceStack: new BSD license.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using ServiceStack.Common;
using ServiceStack.Logging;
using ServiceStack.Text;

namespace ServiceStack.OrmLite
{
    public delegate string GetQuotedValueDelegate(object value, Type fieldType);
    public delegate object ConvertDbValueDelegate(object value, Type type);
    public delegate void PropertySetterDelegate(object instance, object value);
    public delegate object PropertyGetterDelegate(object instance);
    
    public delegate object GetValueDelegate(int i);

    public static class OrmLiteReadExtensions
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(OrmLiteReadExtensions));

        [Conditional("DEBUG")]
        private static void LogDebug(string fmt, params object[] args)
        {
            if (args.Length > 0)
                Log.DebugFormat(fmt, args);
            else
                Log.Debug(fmt);
        }

        private static IDataReader ExecReader(this IDbCommand dbCmd, string sql)
        {
            LogDebug(sql);
            dbCmd.CommandText = sql;
            return dbCmd.ExecuteReader();
        }

		public static GetValueDelegate GetValueFn<T>(IDataRecord reader)
        {
            var type = typeof(T);

            if (type == typeof(string))
                return reader.GetString;

            if (type == typeof(short))
                return i => reader.GetInt16(i);

            if (type == typeof(int))
                return i => reader.GetInt32(i);

            if (type == typeof(long))
                return i => reader.GetInt64(i);

            if (type == typeof(bool))
                return i => reader.GetBoolean(i);

            if (type == typeof(DateTime))
                return i => reader.GetDateTime(i);

            if (type == typeof(Guid))
                return i => reader.GetGuid(i);

            if (type == typeof(float))
                return i => reader.GetFloat(i);

            if (type == typeof(double))
                return i => reader.GetDouble(i);

            if (type == typeof(decimal))
                return i => reader.GetDecimal(i);

            return reader.GetValue;
        }


        public static string ToSelectStatement(this Type tableType)
        {
            return ToSelectStatement(tableType, null);
        }

        public static string ToSelectStatement(this Type tableType, string sqlFilter, params object[] filterParams)
        {
            var sql = new StringBuilder();
            const string SelectStatement = "SELECT ";

            var isFullSelectStatement =
                !string.IsNullOrEmpty(sqlFilter)
                && sqlFilter.Length > SelectStatement.Length
                && sqlFilter.Substring(0, SelectStatement.Length).ToUpper().Equals(SelectStatement);

            if (isFullSelectStatement) return sqlFilter.SqlFormat(filterParams);

            var modelDef = tableType.GetModelDefinition();
            sql.AppendFormat("SELECT {0} FROM \"{1}\"", tableType.GetColumnNames(), modelDef.ModelName);
            if (!string.IsNullOrEmpty(sqlFilter))
            {
                sqlFilter = sqlFilter.SqlFormat(filterParams);
                if (!sqlFilter.StartsWith("ORDER ", StringComparison.InvariantCultureIgnoreCase)
                    && !sqlFilter.StartsWith("LIMIT ", StringComparison.InvariantCultureIgnoreCase))
                {
                    sql.Append(" WHERE ");
                }
                sql.Append(sqlFilter);
            }

            return sql.ToString();
        }

        public static List<T> Select<T>(this IDbCommand dbCmd)
            where T : new()
        {
            return Select<T>(dbCmd, (string)null);
        }

        public static List<T> Select<T>(this IDbCommand dbCmd, string sqlFilter, params object[] filterParams)
            where T : new()
        {
            using (var reader = dbCmd.ExecReader(ToSelectStatement(typeof(T), sqlFilter, filterParams)))
            {
                return reader.ConvertToList<T>();
            }
        }

        public static List<TModel> Select<TModel>(this IDbCommand dbCmd, Type fromTableType)
            where TModel : new()
        {
            return Select<TModel>(dbCmd, fromTableType, null);
        }

        public static List<TModel> Select<TModel>(this IDbCommand dbCmd, Type fromTableType, string sqlFilter, params object[] filterParams)
            where TModel : new()
        {
            var sql = new StringBuilder();
            var modelDef = ModelDefinition<TModel>.Definition;
            sql.AppendFormat("SELECT {0} FROM \"{1}\"", modelDef.GetColumnNames(), fromTableType.GetModelDefinition().ModelName);
            if (!string.IsNullOrEmpty(sqlFilter))
            {
                sqlFilter = sqlFilter.SqlFormat(filterParams);
                sql.Append(" WHERE ");
                sql.Append(sqlFilter);
            }

            using (var reader = dbCmd.ExecReader(sql.ToString()))
            {
                return reader.ConvertToList<TModel>();
            }
        }

        public static IEnumerable<T> Each<T>(this IDbCommand dbCmd)
            where T : new()
        {
            return Each<T>(dbCmd, null);
        }

        public static IEnumerable<T> Each<T>(this IDbCommand dbCmd, string filter, params object[] filterParams)
            where T : new()
        {
            var fieldDefs = ModelDefinition<T>.Definition.FieldDefinitionsArray;
            using (var reader = dbCmd.ExecReader(ToSelectStatement(typeof(T), filter, filterParams)))
            {
                while (reader.Read())
                {
                    var row = new T();
                    row.PopulateWithSqlReader(reader, fieldDefs);
                    yield return row;
                }
            }
        }

        public static T First<T>(this IDbCommand dbCmd, string filter, params object[] filterParams)
            where T : new()
        {
            return First<T>(dbCmd, filter.SqlFormat(filterParams));
        }

        public static T First<T>(this IDbCommand dbCmd, string filter)
            where T : new()
        {
            var result = FirstOrDefault<T>(dbCmd, filter);
            if (Equals(result, default(T)))
            {
                throw new ArgumentNullException(string.Format(
                    "{0}: '{1}' does not exist", typeof(T).Name, filter));
            }
            return result;
        }

        public static T FirstOrDefault<T>(this IDbCommand dbCmd, string filter, params object[] filterParams)
            where T : new()
        {
            return FirstOrDefault<T>(dbCmd, filter.SqlFormat(filterParams));
        }

        public static T FirstOrDefault<T>(this IDbCommand dbCmd, string filter)
            where T : new()
        {
            using (var dbReader = dbCmd.ExecReader(ToSelectStatement(typeof(T), filter)))
            {
                return dbReader.ConvertTo<T>();
            }
        }

        public static T GetById<T>(this IDbCommand dbCmd, object idValue)
            where T : new()
        {
			return First<T>(dbCmd, ModelDefinition<T>.PrimaryKeyName + " = {0}".SqlFormat(idValue));
        }

		public static void SetFilter<T>(this IDbCommand dbCmd, string name, T value)
		{
			dbCmd.Parameters.Clear();
			var p1 = dbCmd.CreateParameter();
			p1.ParameterName = name;
			p1.DbType = DbTypes<T>.DbType;
			p1.Direction = ParameterDirection.Input;
			p1.Value = value;
			dbCmd.Parameters.Add(p1);
		}

		private static void SetFilter<T>(IDbCommand dbCmd, string name, object value)
		{
			dbCmd.Parameters.Clear();
			var p = dbCmd.CreateParameter();
			p.ParameterName = name;
			p.DbType = DbTypes.ColumnDbTypeMap[value.GetType()];
			p.Direction = ParameterDirection.Input;
			dbCmd.Parameters.Add(p);
			dbCmd.CommandText = GetFilterSql(dbCmd, ModelDefinition<T>.Definition);
		}

		public static void SetFilters<T>(this IDbCommand dbCmd, object anonType, bool excludeNulls)
		{
			dbCmd.Parameters.Clear();
			var pis = anonType.GetType().GetSerializableProperties();
			foreach (var pi in pis)
			{
				var mi = pi.GetGetMethod();
				if (mi == null) continue;

				var value = mi.Invoke(anonType, new object[0]);
				if (excludeNulls && value == null) continue;

				var p = dbCmd.CreateParameter();
				p.ParameterName = pi.Name;
				p.DbType = DbTypes.ColumnDbTypeMap[pi.PropertyType];
				p.Direction = ParameterDirection.Input;
				p.Value = value;
				dbCmd.Parameters.Add(p);
			}

            dbCmd.CommandText = GetFilterSql(dbCmd, ModelDefinition<T>.Definition);
		}

    	public static void SetFilters<T>(this IDbCommand dbCmd, object anonType)
		{
            dbCmd.SetFilters<T>(anonType, false);
		}
        
        public static void AddIdFilter<T>(this IDbCommand dbCmd, T value)
        {
			SetFilter(dbCmd, ModelDefinition<T>.Definition.PrimaryKey.Name, value);
        }

		public static void ClearFilters(this IDbCommand dbCmd)
		{
			dbCmd.Parameters.Clear();			
		}

        private static string GetFilterSql(IDbCommand dbCmd, ModelDefinition modelDef) 
        {
			var sb = new StringBuilder(modelDef.SqlSelectAllFromTable);
            for (var i = 0; i < dbCmd.Parameters.Count; i++)
            {
            	sb.Append(i == 0 ? " WHERE " : " AND ");
                var p = (IDbDataParameter) dbCmd.Parameters[i];
                sb.AppendLine(p.ParameterName + " = @" + p.ParameterName);
            }
            return sb.ToString();
        }

        public static T QueryById<T>(this IDbCommand dbCmd, object value)
            where T : new()
        {
			if (dbCmd.Parameters.Count != 1 || 
				((IDbDataParameter)dbCmd.Parameters[0]).ParameterName != ModelDefinition<T>.PrimaryKeyName)
				SetFilter<T>(dbCmd, ModelDefinition<T>.PrimaryKeyName, value);

            ((IDbDataParameter)dbCmd.Parameters[0]).Value = value;

            using (var dbReader = dbCmd.ExecuteReader())
            {
                return dbReader.ConvertTo<T>();
            }
        }

        public static T QuerySingle<T>(this IDbCommand dbCmd, string name, object value)
            where T : new()
        {
			if (dbCmd.Parameters.Count != 1 || ((IDbDataParameter)dbCmd.Parameters[0]).ParameterName != name)
				SetFilter<T>(dbCmd, name, value);

            ((IDbDataParameter)dbCmd.Parameters[0]).Value = value;

            using (var dbReader = dbCmd.ExecuteReader())
            {
                return dbReader.ConvertTo<T>();
            }
        }

        public static T QuerySingle<T>(this IDbCommand dbCmd, object anonType)
            where T : new()
        {
            dbCmd.SetFilters<T>(anonType);

            using (var dbReader = dbCmd.ExecuteReader())
            {
                return dbReader.ConvertTo<T>();
            }
        }

		public static List<T> Query<T>(this IDbCommand dbCmd, string name, object value)
			where T : new()
		{
			if (dbCmd.Parameters.Count != 1 || ((IDbDataParameter)dbCmd.Parameters[0]).ParameterName != name)
				SetFilter<T>(dbCmd, name, value);

            ((IDbDataParameter)dbCmd.Parameters[0]).Value = value;

			using (var dbReader = dbCmd.ExecuteReader())
			{
				return dbReader.ConvertToList<T>();
			}
		}

		public static List<T> Query<T>(this IDbCommand dbCmd, object anonType)
			where T : new()
		{
            dbCmd.SetFilters<T>(anonType);

			using (var dbReader = dbCmd.ExecuteReader())
			{
				return dbReader.ConvertToList<T>();
			}
		}

		public static List<T> QueryByExample<T>(this IDbCommand dbCmd, object anonType)
			where T : new()
		{
            dbCmd.SetFilters<T>(anonType, true);

			using (var dbReader = dbCmd.ExecuteReader())
			{
				return dbReader.ConvertToList<T>();
			}
		}

		public static IEnumerable<T> QueryEach<T>(this IDbCommand dbCmd, object anonType)
			where T : new()
		{
			dbCmd.SetFilters<T>(anonType);

			var fieldDefs = ModelDefinition<T>.Definition.FieldDefinitionsArray;
			using (var reader = dbCmd.ExecuteReader())
			{
				while (reader.Read())
				{
					var row = new T();
					row.PopulateWithSqlReader(reader, fieldDefs);
					yield return row;
				}
			}
		}

        public static T GetByIdOrDefault<T>(this IDbCommand dbCmd, object idValue)
            where T : new()
        {
            return FirstOrDefault<T>(dbCmd, ModelDefinition<T>.PrimaryKeyName + " = {0}".SqlFormat(idValue));
        }

        public static List<T> GetByIds<T>(this IDbCommand dbCmd, IEnumerable idValues)
            where T : new()
        {
            var sql = idValues.GetIdsInSql();
            if (sql == null) return new List<T>();

			return Select<T>(dbCmd, ModelDefinition<T>.PrimaryKeyName + " IN (" + sql + ")");
        }

        public static T GetScalar<T>(this IDbCommand dbCmd, string sql, params object[] sqlParams)
        {
            using (var reader = dbCmd.ExecReader(sql.SqlFormat(sqlParams)))
            {
                return GetScalar<T>(reader);
            }
        }

        public static T GetScalar<T>(this IDataReader reader)
        {
            while (reader.Read())
            {
                return TypeSerializer.DeserializeFromString<T>(reader.GetValue(0).ToString());
            }
            return default(T);
        }

        public static long GetLastInsertId(this IDbCommand dbCmd)
        {
            return OrmLiteConfig.DialectProvider.GetLastInsertId(dbCmd);
        }

        public static List<T> GetFirstColumn<T>(this IDbCommand dbCmd, string sql, params object[] sqlParams)
        {
            using (var dbReader = dbCmd.ExecReader(sql.SqlFormat(sqlParams)))
            {
                return GetFirstColumn<T>(dbReader);
            }
        }

        public static List<T> GetFirstColumn<T>(this IDataReader reader)
        {
            var columValues = new List<T>();
            var getValueFn = GetValueFn<T>(reader);
            while (reader.Read())
            {
                var value = getValueFn(0);
                columValues.Add((T)value);
            }
            return columValues;
        }

        public static HashSet<T> GetFirstColumnDistinct<T>(this IDbCommand dbCmd, string sql, params object[] sqlParams)
        {
            using (var dbReader = dbCmd.ExecReader(sql.SqlFormat(sqlParams)))
            {
                return GetFirstColumnDistinct<T>(dbReader);
            }
        }

        public static HashSet<T> GetFirstColumnDistinct<T>(this IDataReader reader)
        {
            var columValues = new HashSet<T>();
            var getValueFn = GetValueFn<T>(reader);
            while (reader.Read())
            {
                var value = getValueFn(0);
                columValues.Add((T)value);
            }
            return columValues;
        }

        public static Dictionary<K, List<V>> GetLookup<K, V>(this IDbCommand dbCmd, string sql, params object[] sqlParams)
        {
            using (var dbReader = dbCmd.ExecReader(sql.SqlFormat(sqlParams)))
            {
                return GetLookup<K, V>(dbReader);
            }
        }

        public static Dictionary<K, List<V>> GetLookup<K, V>(this IDataReader reader)
        {
            var lookup = new Dictionary<K, List<V>>();

        	var getKeyFn = GetValueFn<K>(reader);
            var getValueFn = GetValueFn<V>(reader);
            while (reader.Read())
            {
                var key = (K)getKeyFn(0);
                var value = (V)getValueFn(1);

            	List<V> values;
            	if (!lookup.TryGetValue(key, out values))
                {
                    values = new List<V>();
                    lookup[key] = values;
                }
                values.Add(value);
            }

            return lookup;
        }

        public static Dictionary<K, V> GetDictionary<K, V>(this IDbCommand dbCmd, string sql, params object[] sqlParams)
        {
            using (var dbReader = dbCmd.ExecReader(sql.SqlFormat(sqlParams)))
            {
                return GetDictionary<K, V>(dbReader);
            }
        }

        public static Dictionary<K, V> GetDictionary<K, V>(this IDataReader reader)
        {
            var map = new Dictionary<K, V>();

            var getKeyFn = GetValueFn<K>(reader);
            var getValueFn = GetValueFn<V>(reader);
            while (reader.Read())
            {
                var key = (K)getKeyFn(0);
                var value = (V)getValueFn(1);

                map.Add(key, value);
            }

            return map;
        }
    }
}