using System.Collections.Generic;
using System;

namespace DbChronicle.Demo.OperationTests.Cases
{
    internal static class OperationCaseCatalog
    {
        public static IReadOnlyList<OperationTestCase> Create(string dialect)
        {
            switch (dialect)
            {
                case "mysql":
                    return MySqlOperationCases.Create();

                case "postgresql":
                    return PostgreSqlOperationCases.Create();

                case "sqlite":
                    return SqliteOperationCases.Create();

                default:
                    throw new NotSupportedException("没有该方言的操作用例：" + dialect);
            }
        }
    }
}
