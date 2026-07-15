using AutoMapper;

using Mango.MessageBus;
using Mango.Services.ShoppingCartAPI.Data;
using Mango.Services.ShoppingCartAPI.Models;
using Mango.Services.ShoppingCartAPI.Models.Dto;
using Mango.Services.ShoppingCartAPI.Service.IService;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using System.Reflection.PortableExecutable;

namespace Mango.Services.ShoppingCartAPI.Controllers
{
    [Route("api/cart")]
    [ApiController]
    public class CartAPIController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IMapper _mapper;
        private readonly IMessageProducer _messageBus;

        /// <summary>
        /// Dont Make it private readonly, else it will be null.
        /// </summary>
        private IProductService _productService;
        private ICouponService _couponService;
        private IConfiguration _configuration;
        private readonly ResponseDto _response;

        public CartAPIController(AppDbContext db, IMapper mapper
                                , IProductService productService
                                , ICouponService couponService
                                , IMessageProducer messageBus
                                , IConfiguration configuration)
        {
            _db = db;
            _mapper = mapper;
            _messageBus = messageBus;
            _productService = productService;
            _couponService = couponService;
            _configuration = configuration;
            _response = new ResponseDto();
            _configuration = configuration;
        }

        [HttpGet("GetCart/{userId}")]
        public async Task<ResponseDto> GetCart(string userId)
        {
            try
            {
                CartDto cart = new CartDto()
                {
                    CartHeader = _mapper.Map<CartHeaderDto>(_db.CartHeaders.FirstOrDefault(u => u.UserId == userId))
                };

                if (cart.CartHeader != null)
                {

                    cart.CartDetails = _mapper.Map<IEnumerable<CartDetailsDto>>(_db.CartDetails
                                    .Where(u => u.CartHeaderId == cart.CartHeader.CartHeaderId));

                    /// Why to fetch all products? Improvement scope here. 
                    /// TODO: Improve loading of products in cart.
                    IEnumerable<ProductDto> productDtos = await _productService.GetProducts();

                    foreach (var item in cart.CartDetails)
                    {
                        /// ProductId must be avaialable in database, else it will fail.
                        /// This is tight coupling with database.
                        /// SDE Observation.
                        item.Product = productDtos.FirstOrDefault(u => u.Id == item.ProductId);

                        /// Apply coupon here. Fetch Coupon details, if available and active
                        /// compute final prices accordingly
                        /// update tha cart and return. Checking coupon here enables us to
                        /// apply coupon productwise.

                        cart.CartHeader.CartTotal += (item.Count * item.Product.Price);
                    }

                    if (!string.IsNullOrEmpty(cart.CartHeader.CouponCode))
                    {
                        CouponDto coupon = await _couponService.GetCoupon(cart.CartHeader.CouponCode);
                        if (coupon != null && cart.CartHeader.CartTotal > coupon.MinAmount)
                        {
                            cart.CartHeader.CartTotal -= coupon.DiscountAmount;
                            cart.CartHeader.Discount = coupon.DiscountAmount;
                        }
                    }

                }

                _response.Result = cart;
            }
            catch (Exception ex)
            {
                _response.Result = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [HttpPost("ApplyCoupon")]
        public async Task<ResponseDto> ApplyCoupon([FromBody] CartDto cartDto)
        {
            try
            {
                //.AsNoTracking() might need further review
                var cartHeaderFromDb = await _db.CartHeaders.FirstAsync(u => u.UserId == cartDto.CartHeader.UserId);
                cartHeaderFromDb.CouponCode = cartDto.CartHeader.CouponCode;
                _db.CartHeaders.Update(cartHeaderFromDb);
                await _db.SaveChangesAsync();
                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [HttpPost("RemoveCoupon")]
        public async Task<ResponseDto> RemoveCoupon([FromBody] CartDto cartDto)
        {
            try
            {
                //.AsNoTracking() might need further review
                var cartHeaderFromDb = await _db.CartHeaders.FirstAsync(u => u.UserId == cartDto.CartHeader.UserId);
                cartHeaderFromDb.CouponCode = "";
                _db.CartHeaders.Update(cartHeaderFromDb);
                await _db.SaveChangesAsync();
                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        //TODO: Draw relations between all dtos and models related to cart
        [HttpPost("CartUpsert")]
        public async Task<ResponseDto> CartUpsert(CartDto cartDto)
        {
            try
            {
                var cartHeaderFromDb = await _db.CartHeaders.AsNoTracking()
                                                .FirstOrDefaultAsync(u => u.UserId == cartDto.CartHeader.UserId);
                if (cartHeaderFromDb == null)
                {
                    // Create new cart header and details
                    CartHeader cartHeader = _mapper.Map<CartHeader>(cartDto.CartHeader);
                    _db.CartHeaders.Add(cartHeader);
                    await _db.SaveChangesAsync();

                    cartDto.CartDetails.First().CartHeaderId = cartHeader.CartHeaderId;
                    CartDetails cartDetails = _mapper.Map<CartDetails>(cartDto.CartDetails.First());
                    _db.CartDetails.Add(cartDetails);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    // If cart header exists, check for cart details
                    // If details for the product do not exist, create them

                    //TODO: Review this part AsNoTracking() how it can benfit us here

                    var cartDetailsFromDb = await _db.CartDetails.AsNoTracking().FirstOrDefaultAsync(
                        u => u.ProductId == cartDto.CartDetails.First().ProductId
                        && u.CartHeaderId == cartHeaderFromDb.CartHeaderId);

                    if (cartDetailsFromDb == null)
                    {
                        //create cart details
                        cartDto.CartDetails.First().CartHeaderId = cartHeaderFromDb.CartHeaderId;
                        CartDetails cartDetails = _mapper.Map<CartDetails>(cartDto.CartDetails.First());
                        _db.CartDetails.Add(cartDetails);
                        await _db.SaveChangesAsync();
                    }
                    else
                    {
                        //update the count / cart details
                        cartDto.CartDetails.First().Count += cartDetailsFromDb.Count;

                        //TODO: Review if we need to map all properties or just the count
                        cartDto.CartDetails.First().CartHeaderId = cartDetailsFromDb.CartHeaderId;
                        cartDto.CartDetails.First().CartDetailsId = cartDetailsFromDb.CartDetailsId;

                        CartDetails cartDetails = _mapper.Map<CartDetails>(cartDto.CartDetails.First());
                        _db.CartDetails.Update(cartDetails);
                        //_db.SaveChanges();
                        await _db.SaveChangesAsync();
                    }
                }
                _response.Result = cartDto;

                //TODOD: For now, cart values are updating as of UserId provided.
                //Check second last commit for more details before this commit.
                //This works for now with user only client. And no other client present in flow -
                //like inventory management, order management etc.
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [HttpPost("RemoveCart")]
        public async Task<ResponseDto> RemoveCart([FromBody] int cartDetailsId)
        {
            try
            {
                CartDetails cartDetails = _db.CartDetails.First(u => u.CartDetailsId == cartDetailsId);

                int totalCountOfCartItem = _db.CartDetails
                                              .Where(u => u.CartHeaderId == cartDetails.CartHeaderId).Count();

                _db.CartDetails.Remove(cartDetails);
                if (totalCountOfCartItem == 1)
                {
                    var cartHeaderToRemove = await _db.CartHeaders
                                             .FirstOrDefaultAsync(u => u.CartHeaderId == cartDetails.CartHeaderId);

                    _db.CartHeaders.Remove(cartHeaderToRemove);
                }

                await _db.SaveChangesAsync();

                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }

        [HttpPost("EmailCartRequest")]
        public async Task<ResponseDto> EmailCartRequest([FromBody] CartDto cartDto)
        {
            try
            {
                await _messageBus.SendMessage(cartDto, _configuration.GetValue<string>("TopicAndQueueNames:EmailShoppingCartQueue"));
                _response.Result = true;
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.Message = ex.Message;
            }
            return _response;
        }
    }
}
