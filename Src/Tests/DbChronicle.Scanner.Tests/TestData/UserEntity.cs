using System.Collections.Generic;
using System;

namespace DbChronicle.Scanner.Tests
{
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
}