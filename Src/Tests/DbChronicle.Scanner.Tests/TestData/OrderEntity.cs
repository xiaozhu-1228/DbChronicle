namespace DbChronicle.Scanner.Tests
{
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
}
