using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BulkyBookWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUnitOfWork _unitOfWOrk;

        public HomeController(ILogger<HomeController> logger, IUnitOfWork unitOfWOrk)
        {
            _logger = logger;
            _unitOfWOrk = unitOfWOrk;
        }

        public IActionResult Index()
        {
            IEnumerable<Product> productList = _unitOfWOrk.Product.GetAll(includeProperties: "Category,CoverType");

            return View(productList);
        }

        public IActionResult Details(int id)
        {
            ShoppingCart cart = new()
            {
                Product = _unitOfWOrk.Product.GetFirstOrDefault(p => p.Id == id, includeProperties: "Category,CoverType"),
                Count = 1
            };
            return View(cart);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
