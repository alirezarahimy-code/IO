using Dapper;

namespace TD5.Model
{
    public class DapQuery<T>
    {
        public string Query { get; set; }

        public DynamicParameters Params { get; set; }

        public DapType DapType { get; set; }

        public DapQuery(string query, object @params = null, DapType type = DapType.SELECT)
        {
            Query = query ?? string.Empty;
            Params = new DynamicParameters();
            if (@params != null)
            {
                Params.AddDynamicParams(@params);
            }
            DapType = type;
        }

        public DapQuery<T> AddQuery(string query, object @params = null)
        {
            switch (DapType)
            {
                case DapType.SELECT:
                case DapType.DELETE:
                    Query += (Query.Contains("WHERE") ? ("AND " + query + " ") : ("WHERE " + query + " "));
                    break;
                case DapType.UPDATE:
                    Query += (Query.Contains("SET") ? (", " + query + " ") : ("SET " + query + " "));
                    break;
            }
            if (@params != null)
            {
                Params.AddDynamicParams(@params);
            }
            return this;
        }

        public DapQuery<T> AddOrder(string query, bool desc = false)
        {
            Query = Query + " ORDER BY " + query + " ";
            if (desc)
            {
                Query += " DESC";
            }
            return this;
        }
    }
}
