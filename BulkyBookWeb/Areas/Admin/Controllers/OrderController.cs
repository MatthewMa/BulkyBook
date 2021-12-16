using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace BulkyBookWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class OrderController : Controller
    {
        public IUnitOfWork _unitOfWork { get; set; }
        [BindProperty]
        public OrderVM OrderVM { get; set; }
        public OrderController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
        {            
            return View();
        }
        public IActionResult Detail(int orderId)
        {
            OrderVM orderVM = new()
            {
                OrderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(o => o.Id == orderId, includeProperties: "ApplicationUser"),
                OrderDetails = _unitOfWork.OrderDetail.GetAll(o => o.OrderId == orderId, includeProperties: "Product")
            };
            return View(orderVM);
        }
        [HttpPost]    
        [ValidateAntiForgeryToken]
        public IActionResult UpdateOrderDetail()
        {
            var orderFromDb = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked:false);
            orderFromDb.Name = OrderVM.OrderHeader.Name;
            orderFromDb.PhoneNumber = OrderVM.OrderHeader.PhoneNumber;
            orderFromDb.StreetAddress = OrderVM.OrderHeader.StreetAddress;
            orderFromDb.City = OrderVM.OrderHeader.City;
            orderFromDb.State = OrderVM.OrderHeader.State;
            orderFromDb.PostalCode = OrderVM.OrderHeader.PostalCode;
            if (OrderVM.OrderHeader.Carrier != null)
            {
                orderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
            }
            if (OrderVM.OrderHeader.TrackingNumber != null)
            {
                orderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
            }
            _unitOfWork.OrderHeader.Update(orderFromDb);
            _unitOfWork.Save();
            TempData["Success"] = "Order details Updated Successfully.";
            return RedirectToAction("Detail", "Order", new { orderId = orderFromDb.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult StartProcessing()
        {
            var orderFromDb = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);            
            _unitOfWork.OrderHeader.UpdateStatus(orderFromDb.Id, SD.StatusInProcess);
            _unitOfWork.Save();
            TempData["Success"] = "Order status Updated Successfully.";
            return RedirectToAction("Detail", "Order", new { orderId = orderFromDb.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult StartShipping()
        {
            var orderFromDb = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);
            orderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
            orderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
            orderFromDb.OrderStatus = SD.StatusShipped;
            orderFromDb.ShippingDate = System.DateTime.Now;
            if (orderFromDb.OrderStatus == SD.PaymentStatusDelayedPayment)
            {
                orderFromDb.PaymentDueDate = System.DateTime.Now.AddDays(30);
            }
            _unitOfWork.OrderHeader.Update(orderFromDb);
            _unitOfWork.Save();
            TempData["Success"] = "Order status Updated Successfully.";
            return RedirectToAction("Detail", "Order", new { orderId = orderFromDb.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult PayNow()
        {
            OrderVM.OrderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(o => o.Id == OrderVM.OrderHeader.Id, includeProperties: "ApplicationUser");
            OrderVM.OrderDetails = _unitOfWork.OrderDetail.GetAll(o => o.OrderId == OrderVM.OrderHeader.Id, includeProperties: "Product");
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
                SuccessUrl = domain + $"admin/order/PaymentConfirmation?orderHeaderId={OrderVM.OrderHeader.Id}",
                CancelUrl = domain + $"admin/order/detail?orderId={OrderVM.OrderHeader.Id}",
            };

            foreach (var item in OrderVM.OrderDetails)
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
            OrderVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
            OrderVM.OrderHeader.OrderStatus = SD.StatusPending;
            _unitOfWork.OrderHeader.UpdateStatus(OrderVM.OrderHeader.Id, OrderVM.OrderHeader.OrderStatus, OrderVM.OrderHeader.PaymentStatus);
            _unitOfWork.OrderHeader.UpdateStripeStatus(OrderVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWork.Save();
            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);            
        }

        public IActionResult OrderConfirmation(int orderHeaderId)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(o => o.Id == orderHeaderId);
            if (orderHeader.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);
                // Check the stripe status
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStatus(orderHeaderId, orderHeader.OrderStatus, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }           
            return View(orderHeaderId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult CancelOrder()
        {
            var orderFromDb = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);
            if (orderFromDb.OrderStatus == SD.PaymentStatusApproved)
            {
                var option = new RefundCreateOptions
                {
                    Reason = RefundReasons.RequestedByCustomer,
                    PaymentIntent = orderFromDb.PaymentIntentId
                };
                var service = new RefundService();
                var refund = service.Create(option);
                _unitOfWork.OrderHeader.UpdateStatus(orderFromDb.Id, SD.StatusCancelled, SD.StatusRefunded);
            }
            else
            {
                _unitOfWork.OrderHeader.UpdateStatus(orderFromDb.Id, SD.StatusCancelled, SD.StatusRefunded);
            }
            _unitOfWork.Save();            
            TempData["Success"] = "Order status Updated Successfully.";
            return RedirectToAction("Detail", "Order", new { orderId = orderFromDb.Id });
        }

        #region API calls
        [HttpGet]
        public IActionResult GetAll(string status)
        {
            IEnumerable<OrderHeader> orderHeaders;
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
            // Get role info
            if (User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee))
            {
                orderHeaders = _unitOfWork.OrderHeader.GetAll(includeProperties: "ApplicationUser");
            }    
            else
            {
                orderHeaders = _unitOfWork.OrderHeader.GetAll(o => o.ApplicationUserId == claim.Value, includeProperties: "ApplicationUser");
            }
            string orderStatus = "";
            switch (status)
            {
                case "pending":
                    orderStatus = SD.StatusPending;
                    break;
                case "inprocess":
                    orderStatus = SD.StatusInProcess;
                    break;
                case "completed":
                    orderStatus = SD.StatusShipped;
                    break;
                case "approved":
                    orderStatus = SD.StatusApproved;
                    break;                
            }
            if (status != "all")
            {
                orderHeaders = orderHeaders.Where(o => o.OrderStatus == orderStatus);
            }                       
            return Json(new { data = orderHeaders });
        }
        #endregion
    }
}