using System.IO;
using System.Linq;

using DbChronicle.Common.Enums;
using DbChronicle.Scanner.Languages;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    /// <summary>多语言源码扫描：Java（JPA 注解）/ Go（struct tag）/ TypeScript（interface 与装饰器）。</summary>
    public class LanguageScannerTests
    {
        private const string JavaSource = @"
package com.example.entities;

import javax.persistence.*;

@Table(name = ""nor_user"")
public class UserEntity {
    @Id
    @GeneratedValue
    private Long id;

    @Column(name = ""user_name"")
    @MaxLength(64)
    private String userName;

    private BigDecimal amount;

    @Transient
    private String displayName;
}
";

        private const string GoSource = "package entities\n"
            + "\n"
            + "import \"time\"\n"
            + "\n"
            + "type NorOrder struct {\n"
            + "\tID int64 `db:\"id\" json:\"id\"`\n"
            + "\tOrderNo string `db:\"order_no\" json:\"orderNo\"`\n"
            + "\tAmount float64 `db:\"amount\"`\n"
            + "\tCreatedAt time.Time `db:\"created_at\"`\n"
            + "\tIgnored string\n"
            + "}\n";

        private const string TypeScriptSource = "import { Entity, Column, PrimaryGeneratedColumn } from 'typeorm';\n"
            + "\n"
            + "@Entity('nor_user')\n"
            + "export class UserEntity {\n"
            + "  @PrimaryGeneratedColumn()\n"
            + "  id: number;\n"
            + "\n"
            + "  @Column({ name: 'user_name', length: 64 })\n"
            + "  userName: string;\n"
            + "\n"
            + "  @Column()\n"
            + "  active: boolean;\n"
            + "}\n"
            + "\n"
            + "export interface OrderSummary {\n"
            + "  orderNo: string;\n"
            + "  total: number | null;\n"
            + "}\n";

        [Fact]
        public void Java_ShouldMapJpaEntity()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            File.WriteAllText(Path.Combine(directory, "UserEntity.java"), JavaSource);

            var result = new JavaSourceScanner().ScanDirectory(directory);

            var table = result.Tables.Single();
            Assert.Equal("nor_user", table.Name);
            Assert.Equal(3, table.Columns.Count);
            Assert.DoesNotContain(table.Columns, x => x.Name == "displayName");
            Assert.Equal(LogicalType.Int64, table.Columns.Single(x => x.Name == "id").Type);
            Assert.True(table.Columns.Single(x => x.Name == "id").AutoIncrement);
            Assert.Equal("user_name", table.Columns.Single(x => x.Name == "user_name").Name);
            Assert.Equal(64, table.Columns.Single(x => x.Name == "user_name").Length);
            Assert.Equal(LogicalType.Decimal, table.Columns.Single(x => x.Name == "amount").Type);
        }

        [Fact]
        public void Go_ShouldMapStructWithDbTags()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            File.WriteAllText(Path.Combine(directory, "order.go"), GoSource);

            var result = new GoSourceScanner().ScanDirectory(directory);

            var table = result.Tables.Single();
            Assert.Equal("NorOrder", table.Name);
            Assert.Equal(4, table.Columns.Count);
            Assert.DoesNotContain(table.Columns, x => x.Name == "Ignored");
            Assert.Equal(LogicalType.Int64, table.Columns.Single(x => x.Name == "id").Type);
            Assert.Equal(LogicalType.Float64, table.Columns.Single(x => x.Name == "amount").Type);
            Assert.Equal(LogicalType.DateTime, table.Columns.Single(x => x.Name == "created_at").Type);
        }

        [Fact]
        public void TypeScript_ShouldMapEntityAndInterface()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            File.WriteAllText(Path.Combine(directory, "user.ts"), TypeScriptSource);

            var result = new TypeScriptSourceScanner().ScanDirectory(directory);

            var entity = result.Tables.Single(x => x.Name == "nor_user");
            Assert.Equal(3, entity.Columns.Count);
            Assert.Equal(LogicalType.Int64, entity.Columns.Single(x => x.Name == "id").Type);
            Assert.True(entity.Columns.Single(x => x.Name == "id").AutoIncrement);
            Assert.Equal("user_name", entity.Columns.Single(x => x.Name == "user_name").Name);
            Assert.Equal(64, entity.Columns.Single(x => x.Name == "user_name").Length);
            Assert.Equal(LogicalType.Boolean, entity.Columns.Single(x => x.Name == "active").Type);

            var summary = result.Tables.Single(x => x.Name == "OrderSummary");
            Assert.Equal(2, summary.Columns.Count);
            Assert.True(summary.Columns.Single(x => x.Name == "total").Nullable);
        }

        [Fact]
        public void Auto_ShouldMergeAllLanguages()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            File.WriteAllText(Path.Combine(directory, "UserEntity.java"), JavaSource);
            File.WriteAllText(Path.Combine(directory, "order.go"), GoSource);
            File.WriteAllText(Path.Combine(directory, "user.ts"), TypeScriptSource);

            var result = new LanguageSourceScanner().ScanDirectory(directory, "auto");

            Assert.Equal(4, result.Tables.Count);
            Assert.Contains(result.Tables, x => x.Name == "nor_user");
            Assert.Contains(result.Tables, x => x.Name == "NorOrder");
        }

        [Fact]
        public void UnsupportedLanguage_ShouldThrow()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            Assert.Throws<System.NotSupportedException>(() =>
                new LanguageSourceScanner().ScanDirectory(directory, "rust"));
        }
    }
}
