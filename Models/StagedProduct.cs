using System.ComponentModel.DataAnnotations;

namespace PCBuilder.Models
{
    /// <summary>
    /// table StagedProduct：菜單管理頁的「零件暫存區」。從菜單移出的商品整筆存在這裡，
    /// 欄位與 MenuProduct 相同（商品資料的快照），放回菜單時原樣還原。
    /// </summary>
    public class StagedProduct
    {
        [Key]
        public int Id { get; set; }

        public int CategoryId { get; set; }

        public required string CategoryName { get; set; }

        public required string SubcategoryName { get; set; }

        public required string ProductName { get; set; }

        public required string ProductFullText { get; set; }

        public string? ProductUrl { get; set; }

        public int ProductPrice { get; set; }

        public int Qty { get; set; }
    }
}
