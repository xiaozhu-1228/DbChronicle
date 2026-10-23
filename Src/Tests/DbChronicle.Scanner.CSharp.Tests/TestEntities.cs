using System;
using System.Collections.Generic;

namespace DbChronicle.Scanning.Tests
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TableAttribute : Attribute
    {
        public TableAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ColumnAttribute : Attribute
    {
        public ColumnAttribute()
        {
        }

        public ColumnAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }

        public string TypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MaxLengthAttribute : Attribute
    {
        public MaxLengthAttribute(int length)
        {
            Length = length;
        }

        public int Length { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class KeyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NotMappedAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CommentAttribute : Attribute
    {
        public CommentAttribute(string comment)
        {
            Comment = comment;
        }

        public string Comment { get; private set; }
    }

    public enum UserKind
    {
        Normal = 0,
        Vip = 1
    }

    [Table("nor_user")]
    public class UserEntity
    {
        [Key]
        public long Id { get; set; }

        [Column("user_name")]
        [MaxLength(64)]
        [Comment("用户名")]
        public string UserName { get; set; }

        public string Bio { get; set; }

        public decimal Balance { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public Guid Uid { get; set; }

        public byte[] Avatar { get; set; }

        public bool Active { get; set; }

        public UserKind Kind { get; set; }

        public int? LoginCount { get; set; }

        [NotMapped]
        public string DisplayName { get; set; }

        public List<OrderEntity> Orders { get; set; }
    }

    [Table("nor_order")]
    public class OrderEntity
    {
        [Key]
        public long Id { get; set; }

        [Column(TypeName = "VARCHAR(32)")]
        public string OrderNo { get; set; }

        [Column(TypeName = "JSON")]
        public string Payload { get; set; }

        public long UserId { get; set; }
    }

    public class NotAnEntity
    {
        public int Value { get; set; }
    }
}
