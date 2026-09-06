using Microsoft.EntityFrameworkCore;
using PCBuilder.Models;

namespace PCBuilder.Data
{
    /// <summary>
    /// 系統會根據這個檔案執行時，檢查是否有對應的資料庫
    /// </summary>
    /// <seealso cref="Microsoft.EntityFrameworkCore.DbContext" />
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Product> Product { get; set; }

        public DbSet<Category> Category { get; set; }

        public DbSet<Subcategory> Subcategory { get; set; }

        public DbSet<MenuCategory> MenuCategory { get; set; }

        public DbSet<MenuProduct> MenuProduct { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Category → Subcategory 的外鍵關係：明確設定，避免 AsSplitQuery 產生重複子目錄
            modelBuilder.Entity<Subcategory>()
                .HasOne<Category>()
                .WithMany(c => c.Subcategories)
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // MenuProduct 的外鍵關係：明確設定為 Cascade Delete，
            // 確保刪除 MenuCategory 時其下的 MenuProduct 一同被清除
            modelBuilder.Entity<MenuProduct>()
                .HasOne<MenuCategory>()
                .WithMany(c => c.MenuProducts)
                .HasForeignKey(p => p.MenuCategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // 字串欄位長度限制，防止過長資料寫入 SQLite
            modelBuilder.Entity<MenuCategory>()
                .Property(c => c.Name)
                .HasMaxLength(100);

            modelBuilder.Entity<MenuProduct>()
                .Property(p => p.CategoryName)
                .HasMaxLength(100);

            modelBuilder.Entity<MenuProduct>()
                .Property(p => p.SubcategoryName)
                .HasMaxLength(100);

            modelBuilder.Entity<MenuProduct>()
                .Property(p => p.ProductName)
                .HasMaxLength(500);
        }
    }
}