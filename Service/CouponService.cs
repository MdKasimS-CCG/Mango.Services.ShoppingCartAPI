using Mango.Services.ShoppingCartAPI.Models.Dto;
using Mango.Services.ShoppingCartAPI.Service.IService;
using Newtonsoft.Json;

namespace Mango.Services.ShoppingCartAPI.Service
{
    public class CouponService : ICouponService
    {
        private readonly IHttpClientFactory _httpClient;
        public CouponService(IHttpClientFactory httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<CouponDto> GetCoupon(string couponDode)
        {
            var client = _httpClient.CreateClient("Coupon");

            /// Check URL pattern used. Keep not that what endpoint
            /// you are accessin must match URL and its pattern.
            var response = await client.GetAsync($"/api/coupon/GetByCode/{couponDode}");
            
            /// Handle error if coupon not found. Response's Content is empty
            /// makeing resp as null. Checking succces is inavlid for this.
            
            var apiContent = await response.Content.ReadAsStringAsync();
            var resp = JsonConvert.DeserializeObject<ResponseDto>(apiContent);

            if (resp.IsSuccess)
            {
                return JsonConvert.DeserializeObject<CouponDto>(
                    Convert.ToString(resp.Result));
            }
            return new CouponDto();
        }
    }
}
