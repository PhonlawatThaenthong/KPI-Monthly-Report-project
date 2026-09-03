using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace KpiReport.Web.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "How KPI figures are produced and who can see them";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Who to reach for access or data issues";

            return View();
        }
    }
}