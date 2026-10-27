using System.Linq;

using DbChronicle.CodeGen;
using DbChronicle.Common.Enums;
using DbChronicle.Models;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>代码生成（XML → 实体）：四种语言的产物与命名转换。</summary>
    public class CodeGenTests
    {
        private static StructureModel BuildModel()
        {
            var model = new StructureModel();
            var table = new TableModel("nor_user") { Comment = "用户表" };
            table.SetColumn(new ColumnDefinition
            {
                Name = "id",
                Type = LogicalType.Int64,
                Nullable = false,
                PrimaryKey = true,
                AutoIncrement = true
            });
            table.SetColumn(new ColumnDefinition
            {
                Name = "user_name",
                Type = LogicalType.String,
                Length = 64,
                Nullable = false,
                Comment = "用户名"
            });
            table.SetColumn(new ColumnDefinition
            {
                Name = "class",
                Type = LogicalType.String,
                Length = 32,
                Nullable = true
            });
            table.SetColumn(new ColumnDefinition
            {
                Name = "amount",
                Type = LogicalType.Decimal,
                Precision = 18,
                Scale = 2,
                Nullable = true
            });
            table.SetColumn(new ColumnDefinition
            {
                Name = "created_at",
                Type = LogicalType.DateTime,
                Nullable = false
            });
            table.PrimaryKeyColumns.Add("id");

            model.Tables["nor_user"] = table;
            return model;
        }

        [Fact]
        public void CSharp_ShouldGenerateEntityWithAnnotations()
        {
            var files = new EntityCodeGenerator().Generate(BuildModel(), new EntityCodeOptions
            {
                Language = "csharp",
                Namespace = "MyApp.Entities"
            });

            var code = files.Single(x => x.Key == "NorUser.cs").Value;
            Assert.Contains("namespace MyApp.Entities", code);
            Assert.Contains("[Table(\"nor_user\")]", code);
            Assert.Contains("public class NorUser", code);
            Assert.Contains("[Key]", code);
            Assert.Contains("[Column(\"user_name\")]", code);
            Assert.Contains("[MaxLength(64)]", code);
            Assert.Contains("[Required]", code);
            Assert.Contains("public string UserName { get; set; }", code);
            Assert.Contains("public long Id { get; set; }", code);
            Assert.Contains("public decimal? Amount { get; set; }", code);
            Assert.Contains("public DateTime CreatedAt { get; set; }", code);
            // class 是关键字，需 @ 转义
            Assert.Contains("public string? @Class { get; set; }", code);
            Assert.Contains("/// <summary>用户表</summary>", code);
        }

        [Fact]
        public void Java_ShouldGeneratePojo()
        {
            var files = new EntityCodeGenerator().Generate(BuildModel(), new EntityCodeOptions
            {
                Language = "java",
                Namespace = "com.example.entities"
            });

            var code = files.Single(x => x.Key == "NorUser.java").Value;
            Assert.Contains("package com.example.entities;", code);
            Assert.Contains("public class NorUser {", code);
            Assert.Contains("private Long id;", code);
            Assert.Contains("private String userName;", code);
            Assert.Contains("private BigDecimal amount;", code);
            Assert.Contains("public Long getId()", code);
            Assert.Contains("import java.math.BigDecimal;", code);
        }

        [Fact]
        public void Go_ShouldGenerateStructWithTags()
        {
            var files = new EntityCodeGenerator().Generate(BuildModel(), new EntityCodeOptions
            {
                Language = "go",
                Namespace = "entities"
            });

            var code = files.Single(x => x.Key == "NorUser.go").Value;
            Assert.Contains("package entities", code);
            Assert.Contains("type NorUser struct {", code);
            Assert.Contains("`db:\"user_name\" json:\"userName\"`", code);
            Assert.Contains("Id int64", code);
            Assert.Contains("CreatedAt time.Time", code);
        }

        [Fact]
        public void TypeScript_ShouldGenerateInterface()
        {
            var files = new EntityCodeGenerator().Generate(BuildModel(), new EntityCodeOptions
            {
                Language = "typescript"
            });

            var code = files.Single(x => x.Key == "NorUser.ts").Value;
            Assert.Contains("export interface NorUser {", code);
            Assert.Contains("id: number;", code);
            Assert.Contains("userName: string;", code);
            Assert.Contains("amount: number | null;", code);
            Assert.Contains("createdAt: Date;", code);
        }

        [Fact]
        public void Cpp_ShouldGenerateHeaderWithOptionalAndKeywords()
        {
            var files = new EntityCodeGenerator().Generate(BuildModel(), new EntityCodeOptions
            {
                Language = "cpp",
                Namespace = "myapp::entities"
            });

            var code = files.Single(x => x.Key == "NorUser.h").Value;
            Assert.Contains("#pragma once", code);
            Assert.Contains("namespace myapp::entities {", code);
            Assert.Contains("struct NorUser {", code);
            Assert.Contains("static constexpr const char* kTableName = \"nor_user\";", code);
            Assert.Contains("std::int64_t id = 0;", code);
            Assert.Contains("std::string user_name;", code);
            Assert.Contains("std::optional<double> amount;", code);
            Assert.Contains("std::chrono::system_clock::time_point created_at;", code);
            Assert.Contains("#include <chrono>", code);
            // class 是 C++ 关键字，成员需改名
            Assert.Contains("std::optional<std::string> class_;", code);
            // 聚合头
            Assert.Contains("all_entities.h", files.Select(x => x.Key));
            Assert.Contains("#include \"NorUser.h\"", files.Single(x => x.Key == "all_entities.h").Value);
        }

        [Fact]
        public void UnsupportedLanguage_ShouldThrow()
        {
            Assert.Throws<System.NotSupportedException>(() =>
                new EntityCodeGenerator().Generate(BuildModel(), new EntityCodeOptions { Language = "cobol" }));
        }

        [Theory]
        [InlineData("nor_user", "NorUser", "norUser")]
        [InlineData("USER_NAME", "UserName", "userName")]
        [InlineData("a-b-c", "ABC", "aBC")]
        public void Naming_ShouldConvertSnakeCaseToPascalAndCamel(string input, string pascal, string camel)
        {
            Assert.Equal(pascal, CodeNaming.ToPascalCase(input));
            Assert.Equal(camel, CodeNaming.ToCamelCase(input));
        }
    }
}