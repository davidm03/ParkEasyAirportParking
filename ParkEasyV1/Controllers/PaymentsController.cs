﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using ParkEasyV1.Models;
using ParkEasyV1.Models.ViewModels;
using Stripe;

namespace ParkEasyV1.Controllers
{
    public class PaymentsController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();

        // GET: Payments
        public ActionResult Index()
        {
            var payments = db.Payments.Include(p => p.User);
            return View(payments.ToList());
        }

        // GET: Payments/Charge
        public ActionResult Charge()
        {
            ViewBag.StripePublishableKey = ConfigurationManager.AppSettings["StripePublishableKey"];

            Booking booking = db.Bookings.Find(TempData["bookingID"]);

            ViewBag.Total = booking.Total;
            ViewBag.StripeTotal = (int)booking.Total*100;

            TempData["bID"] = booking.ID;
            return View();
        }

        // POST: Payments/Charge
        [HttpPost]
        public ActionResult Charge(string stripeEmail, string stripeToken)
        {
            Booking booking = db.Bookings.Find(TempData["bID"]);

            var customers = new CustomerService();
            var charges = new ChargeService();

            var customer = customers.Create(new CustomerCreateOptions
            {
                Email = stripeEmail,
                SourceToken = stripeToken,
            });

            var charge = charges.Create(new ChargeCreateOptions
            {
                Amount = (int)booking.Total * 100,
                Description = "ParkEasy Airport Parking Charge",
                Currency = "gbp",
                CustomerId = customer.Id,
                ReceiptEmail = customer.Email,
            });

            booking.BookingStatus = BookingStatus.Confirmed;

            db.Payments.Add(new StripePayment()
            {
                PaymentDate = DateTime.Now,
                Amount = charge.Amount,
                User = booking.User,
                TransactionID = charge.ReceiptNumber
            });

            db.SaveChanges();

            return RedirectToAction("MyBookings", "Users");
        }

        // GET: Payments/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Payment payment = db.Payments.Find(id);
            if (payment == null)
            {
                return HttpNotFound();
            }
            return View(payment);
        }
        

        // POST: Payments/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "ID, PaymentDate, Amount")] Payment payment)  //remember date, amount, user
        {
            UserManager<User> userManager = new UserManager<User>(new UserStore<User>(db));

            if (ModelState.IsValid)
            {
                //payment.PaymentDate = DateTime.Now;

                //Booking booking = db.Bookings.Find(TempData["bookingID"]);

                //payment.User = userManager.FindByName(User.Identity.Name);

                //booking.BookingStatus = BookingStatus.Confirmed;

                db.Payments.Add(payment);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.UserID = new SelectList(db.Users, "Id", "FirstName", payment.UserID);
            return View(payment);
        }                     


        // GET: Payments/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Payment payment = db.Payments.Find(id);
            if (payment == null)
            {
                return HttpNotFound();
            }
            ViewBag.UserID = new SelectList(db.Users, "Id", "FirstName", payment.UserID);
            return View(payment);
        }

        // POST: Payments/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "ID,PaymentDate,Amount,UserID")] Payment payment)
        {
            if (ModelState.IsValid)
            {
                db.Entry(payment).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.UserID = new SelectList(db.Users, "Id", "FirstName", payment.UserID);
            return View(payment);
        }

        // GET: Payments/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Payment payment = db.Payments.Find(id);
            if (payment == null)
            {
                return HttpNotFound();
            }
            return View(payment);
        }

        // POST: Payments/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Payment payment = db.Payments.Find(id);
            db.Payments.Remove(payment);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
