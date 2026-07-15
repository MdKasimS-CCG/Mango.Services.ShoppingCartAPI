namespace Mango.Services.ShoppingCartAPI.Models.Dto
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        //TODO: Image uploading part is missing. Do yourself
        public string ImageUrl { get; set; }
        public string CategoryName { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
