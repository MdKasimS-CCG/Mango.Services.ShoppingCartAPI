using System.ComponentModel.DataAnnotations;

namespace Mango.Services.ShoppingCartAPI.Models
{
    public class Product
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [Range(1, 1000)]
        public double Price { get; set; }
        //TODO: Image uploading part is missing. Do yourself
        public string ImageUrl { get; set; }
        public string CategoryName { get; set; }
        public string Description { get; set; }
    }
}
