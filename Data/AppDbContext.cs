using Mango.Services.ShoppingCartAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace Mango.Services.ShoppingCartAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<CartHeader> CartHeaders { get; set; } = null!;
        public DbSet<CartDetails> CartDetails { get; set; } = null!;

        /// We can use this method to configure the model further if needed.
        /// For now, it's commented out as we don't have additional configurations.
        /// Let's keep it here for future reference.

        //protected override void OnModelCreating(ModelBuilder modelBuilder){}
        
}
}
