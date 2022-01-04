using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace BulkyBookWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailSender _emailSender;
        [BindProperty]
        public ShoppingCartVM ShoppingCartVM { get; set; }
        public CartController(IUnitOfWork unitOfWork, IEmailSender emailSender)
        {
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
        }
        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            ShoppingCartVM = new ShoppingCartVM()
            {
                ListCart = _unitOfWork.ShoppingCart.GetShoppingCartByUserApplicationId(claim.Value),
                OrderHeader = new()
            };
            double totalPrice = 0;
            foreach (var item in ShoppingCartVM.ListCart)
            {
                double price = 0;
                if (item.Count <= 50)
                    price = item.Product.Price;
                else if (item.Count <= 100)
                    price = item.Product.Price50;
                else
                    price = item.Product.Price100;
                totalPrice += price * item.Count;
            }
            ShoppingCartVM.OrderHeader.OrderTotal = totalPrice;
            return View(ShoppingCartVM);
        }
        public IActionResult Summary()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            ShoppingCartVM = new ShoppingCartVM()
            {
                ListCart = _unitOfWork.ShoppingCart.GetShoppingCartByUserApplicationId(claim.Value),
                OrderHeader = new()
            };
            ShoppingCartVM.OrderHeader.ApplicationUser = _unitOfWork.ApplicationUser.GetFirstOrDefault(u => u.Id == claim.Value);
            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;
            ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State;
            ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode;
            double totalPrice = 0;
            foreach (var item in ShoppingCartVM.ListCart)
            {
                double price = 0;
                if (item.Count <= 50)
                    price = item.Product.Price;
                else if (item.Count <= 100)
                    price = item.Product.Price50;
                else
                    price = item.Product.Price100;
                totalPrice += price * item.Count;
            }
            ShoppingCartVM.OrderHeader.OrderTotal = totalPrice;
            return View(ShoppingCartVM);
        }
        public IActionResult Plus(int cartId)
        {
            var cart = _unitOfWork.ShoppingCart.GetFirstOrDefault(c => c.Id == cartId);
            if (cart == null)
                return View(nameof(Index));
            _unitOfWork.ShoppingCart.IncrementCount(cart, 1);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }
        public IActionResult Minus(int cartId)
        {
            var cart = _unitOfWork.ShoppingCart.GetFirstOrDefault(c => c.Id == cartId);
            if (cart == null)
                return View(nameof(Index));
            _unitOfWork.ShoppingCart.DecrementCount(cart, 1);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }
        public IActionResult Remove(int cartId)
        {
            var cart = _unitOfWork.ShoppingCart.GetFirstOrDefault(c => c.Id == cartId);
            if (cart == null)
                return View(nameof(Index));
            _unitOfWork.ShoppingCart.Remove(cart);
            _unitOfWork.Save();
            var count = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).ToList().Count();
            HttpContext.Session.SetInt32(SD.SessionCart, count);            
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult PlaceOrder()
        {

            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            ShoppingCartVM.ListCart = _unitOfWork.ShoppingCart.GetShoppingCartByUserApplicationId(claim.Value);            
            ShoppingCartVM.OrderHeader.ApplicationUserId = claim.Value;
            double totalPrice = 0;
            foreach (var item in ShoppingCartVM.ListCart)
            {
                double price = 0;
                if (item.Count <= 50)
                    price = item.Product.Price;
                else if (item.Count <= 100)
                    price = item.Product.Price50;
                else
                    price = item.Product.Price100;
                totalPrice += price * item.Count;
            }
            ShoppingCartVM.OrderHeader.OrderTotal = totalPrice;
            // Add to order header
            _unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
            _unitOfWork.Save();
            // Add to order detail
            foreach (var item in ShoppingCartVM.ListCart.ToList())
            {
                OrderDetail orderDetail = new()
                { 
                    OrderId = ShoppingCartVM.OrderHeader.Id,
                    ProductId = item.ProductId,
                    Count = item.Count,
                    Price = (item.Count <= 50 ? item.Product.Price * item.Count : (item.Count <= 100) ? item.Product.Price50 * item.Count : item.Product.
                            Price100 * item.Count)
                };
                _unitOfWork.OrderDetail.Add(orderDetail);
                _unitOfWork.Save();
            }
            var allplicationUser = _unitOfWork.ApplicationUser.GetFirstOrDefault(u => u.Id == claim.Value);
            if (allplicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                // Non-company user
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
                //stripe settings 
                var domain = "https://localhost:44300/";
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string>
                {
                  "card",
                },
                    LineItems = new List<SessionLineItemOptions>(),
                    Mode = "payment",
                    SuccessUrl = domain + $"customer/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}",
                    CancelUrl = domain + $"customer/cart/index",
                };

                foreach (var item in ShoppingCartVM.ListCart)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)((item.Count <= 50 ? item.Product.Price : (item.Count <= 100) ? item.Product.Price50 : item.Product.
                                Price100) * 100),//20.00 -> 2000
                            Currency = "cad",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Product.Title
                            },
                        },
                        Quantity = item.Count,
                    };
                    options.LineItems.Add(sessionLineItem);
                }

                var service = new SessionService();
                Session session = service.Create(options);
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
                _unitOfWork.OrderHeader.UpdateStatus(ShoppingCartVM.OrderHeader.Id, ShoppingCartVM.OrderHeader.OrderStatus, ShoppingCartVM.OrderHeader.PaymentStatus);
                _unitOfWork.OrderHeader.UpdateStripeStatus(ShoppingCartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
                _unitOfWork.Save();
                Response.Headers.Add("Location", session.Url);
                return new StatusCodeResult(303);
            }
            else
            {
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
                _unitOfWork.OrderHeader.UpdateStatus(ShoppingCartVM.OrderHeader.Id, ShoppingCartVM.OrderHeader.OrderStatus, ShoppingCartVM.OrderHeader.PaymentStatus);
                _unitOfWork.Save();
            }
            return RedirectToAction("OrderConfirmation", "Cart", new { id=ShoppingCartVM.OrderHeader.Id });       
        }
        public IActionResult OrderConfirmation(int id)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(o => o.Id == id, includeProperties: "ApplicationUser");
            if (orderHeader.PaymentStatus != SD.PaymentStatusDelayedPayment)
            {
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);
                // Check the stripe status
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }
            _emailSender.SendEmailAsync(orderHeader.ApplicationUser.Email, "New Order - Bulky Book", "<p>New Order Created.</p>");
            var shoppingCarts = _unitOfWork.ShoppingCart.GetShoppingCartByUserApplicationId(orderHeader.ApplicationUserId);
            _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
            _unitOfWork.Save();
            return View(id);
        }
    }
}
