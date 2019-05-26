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
using Itenso.TimePeriod;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Newtonsoft.Json;
using ParkEasyV1.Models;
using ParkEasyV1.Models.ViewModels;
using Rotativa;

namespace ParkEasyV1.Controllers
{
    /// <summary>
    /// Controller for handling all booking events
    /// </summary>
    public class BookingsController : Controller
    {
        /// <summary>
        /// Global instance of ApplicationDbContext
        /// </summary>
        private ApplicationDbContext db = new ApplicationDbContext();

        /// <summary>
        /// HttpGet ActionResult for returning Bookings Index
        /// </summary>
        /// <returns>Index view</returns>
        public ActionResult Index()
        {
            var bookings = db.Bookings.Include(b => b.Flight).Include(b => b.Invoice).Include(b => b.ParkingSlot).Include(b => b.Tariff).Include(b => b.User);
            return View(bookings.ToList());
        }

        /// <summary>
        /// HttpGet ActionResult return the Manage view with a collection of bookings
        /// </summary>
        /// <returns>Manage view with collection of bookings</returns>
        // GET: Bookings/Manage
        [Authorize(Roles = "Admin, Manager, Invoice Clerk, Booking Clerk")]
        public ActionResult Manage()
        {
            foreach (var user in db.Users.ToList())
            {
                if (user.Email.Equals(User.Identity.Name))
                {
                    ViewBag.UserID = user.Id;
                }
            }

            return View(db.Bookings.OrderBy(b => b.DateBooked).ToList());
        }

        // GET: Bookings/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Booking booking = db.Bookings.Find(id);
            if (booking == null)
            {
                return HttpNotFound();
            }
            return View(booking);
        }

        /// <summary>
        /// HttpGet ActionResult to return create booking view
        /// </summary>
        /// <returns>Create view</returns>
        // GET: Bookings/Create
        [Authorize]
        public ActionResult Create()
        {
            if (User.IsInRole("Customer"))
            {
                //declare instance of usermanager
                UserManager<User> userManager = new UserManager<User>(new UserStore<User>(db));

                User currentUser = userManager.FindByEmail(User.Identity.GetUserName());

                CreateBookingViewModel model = new CreateBookingViewModel
                {
                    FirstName = currentUser.FirstName,
                    Surname = currentUser.LastName,
                    Email = currentUser.Email,
                    AddressLine1 = currentUser.AddressLine1,
                    AddressLine2 = currentUser.AddressLine2,
                    City = currentUser.City,
                    Postcode = currentUser.Postcode,
                    PhoneNo = currentUser.PhoneNumber,
                    DepartureDate = DateTime.Today,
                    ReturnDate = DateTime.Today.AddDays(7)
                };


                if (TempData["AvailabilityModel"] as AvailabilityViewModel != null)
                {
                    AvailabilityViewModel availabilityModel = TempData["AvailabilityModel"] as AvailabilityViewModel;

                    model.DepartureDate = availabilityModel.DepartureDate;
                    model.DepartureTime = availabilityModel.DepartureTime;
                    model.ReturnDate = availabilityModel.ReturnDate;
                    model.ReturnTime = availabilityModel.ReturnTime;                                
                }

                return View(model);
            }
            return View();
        }

        
        //// POST: Bookings/Create        
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public ActionResult Create([Bind(Include = "ID,DateBooked,Duration,Total,BookingStatus,ValetService,CheckedIn,CheckedOut,UserID,FlightID,ParkingSlotID,TariffID")] Booking booking)
        //{
        //    if (ModelState.IsValid)
        //    {
        //        db.Bookings.Add(booking);
        //        db.SaveChanges();
        //        return RedirectToAction("Index");
        //    }
           
        //    return View(booking);
        //}

        /// <summary>
        /// HttpPost ActionResult for creating a booking with details provided by a user
        /// </summary>
        /// <param name="model">CreateBookingViewModel with user booking data</param>
        /// <returns>Valet option view</returns>
        // POST: /Bookings/CreateBooking
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CreateBookingViewModel model)
        {
            //declare instance of usermanager
            UserManager<User> userManager = new UserManager<User>(new UserStore<User>(db));

            //get Google reCaptcha API response
            CaptchaResponse response = ValidateCaptcha(Request["g-recaptcha-response"]);

            //if Google reCaptcha API response and model are valid
            if (response.Success && ModelState.IsValid)
            {
                //get the TimeRange for the selected dates
                TimeRange selectedTimeRange = new TimeRange(
                new DateTime(model.DepartureDate.Year, model.DepartureDate.Month, model.DepartureDate.Day, model.DepartureTime.Hours, model.DepartureTime.Minutes, 0),
                new DateTime(model.ReturnDate.Year, model.ReturnDate.Month, model.ReturnDate.Day, model.ReturnTime.Hours, model.ReturnTime.Minutes, 0));

                //if available parking space is NOT 0
                if (FindAvailableParkingSlot(selectedTimeRange)!=0)
                {
                    //create customer vehicle
                    Vehicle vehicle = db.Vehicles.Add(new Vehicle()
                    {
                        RegistrationNumber = model.VehicleRegistration,
                        Make = model.VehicleMake,
                        Model = model.VehicleModel,
                        Colour = model.VehicleModel,
                        NoOfPassengers = model.NoOfPassengers
                    });

                    //create customer flight
                    Flight flight = db.Flights.Add(new Flight()
                    {
                        DepartureFlightNo = model.DepartureFlightNo,
                        DepartureTime = model.DepartureTime,
                        ReturnFlightNo = model.ReturnFlightNo,
                        ReturnFlightTime = model.ReturnTime,
                        DepartureDate = model.DepartureDate,
                        ReturnDate = model.ReturnDate,
                        DestinationAirport = model.DestinationAirport
                    });

                    db.SaveChanges();

                    //try to find the booking user by email from booking form
                    User bookingUser = userManager.FindByEmail(model.Email);

                    //if user cannot be found via email from booking form
                    if (bookingUser == null)
                    {
                        //find the current logged in user
                        bookingUser = userManager.FindByName(User.Identity.Name);
                    }

                    //create customer booking
                    Booking booking = db.Bookings.Add(new Booking()
                    {
                        User = bookingUser,
                        Flight = db.Flights.Find(flight.ID),
                        ParkingSlot = db.ParkingSlots.Find(FindAvailableParkingSlot(selectedTimeRange)),
                        Tariff = db.Tariffs.Find(1),
                        DateBooked = DateTime.Now,
                        Duration = CalculateBookingDuration(model.DepartureDate, model.ReturnDate),
                        Total = db.Tariffs.Find(1).Amount * Convert.ToInt32(CalculateBookingDuration(model.DepartureDate, model.ReturnDate)),
                        BookingStatus = BookingStatus.Unpaid,
                        ValetService = false,
                        CheckedIn = false,
                        CheckedOut = false,
                    });

                    //save database changes
                    db.SaveChanges();

                    //create a new booking line for the booking with customer vehicle
                    booking.BookingLines = new List<BookingLine>() { new BookingLine() { Booking = db.Bookings.Find(booking.ID), Vehicle = db.Vehicles.Find(vehicle.ID) } };

                    //update the contact phone number on the user's account from the booking form
                    bookingUser.PhoneNumber = model.PhoneNo;

                    //save database changes
                    db.SaveChanges();

                    //store the booking id in a TempData
                    TempData["bookingID"] = booking.ID;

                    //return the Valet options view to the user
                    return RedirectToAction("Valet");
                }
                //if available parking space id = 0 (no spaces are available)
                else                
                {
                    //return view with error message
                    TempData["Error"] = "No Parking Slots Available For Your Selected Dates. Please Enter New Dates And Try Again.";
                    return View(model);
                }

            }
            else if(response.Success==false)
            {
                return Content("Error From Google ReCaptcha : " + response.ErrorMessage[0].ToString());
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        /// <summary>
        /// Function to find the next available parking slot for the booking period selected by the user, and return the id of the available parking slot
        /// </summary>
        /// <param name="selectedTimeRange">Booking date period selected by user</param>
        /// <returns>Available parking slot id</returns>
        private int FindAvailableParkingSlot(TimeRange selectedTimeRange)
        {
            //loop through all parking slots
            foreach (var slot in db.ParkingSlots.ToList())
            {
                //set the overlap counter to 0
                int overlapCounter = 0;

                //loop through all bookings associated with parking slot
                foreach (var slotBooking in slot.Bookings.ToList())
                {
                    //create a TimeRange variable to hold the range of dates of the booking
                    TimeRange bookingRange = new TimeRange(
                    new DateTime(
                        slotBooking.Flight.DepartureDate.Year,
                        slotBooking.Flight.DepartureDate.Month,
                        slotBooking.Flight.DepartureDate.Day,
                        slotBooking.Flight.DepartureTime.Hours,
                        slotBooking.Flight.DepartureTime.Minutes,
                        0),
                    new DateTime(
                        slotBooking.Flight.ReturnDate.Year,
                        slotBooking.Flight.ReturnDate.Month,
                        slotBooking.Flight.ReturnDate.Day,
                        slotBooking.Flight.ReturnFlightTime.Hours,
                        slotBooking.Flight.ReturnFlightTime.Minutes,
                        0));

                    //check if the booking time range overlaps with the user's selected booking time range
                    if (selectedTimeRange.OverlapsWith(bookingRange))
                    {
                        //if there is an overlap, then there is already a booking in this parking slot during the user's selected date period
                        //update the overlap counter
                        overlapCounter++;
                    }
                }

                //if there are no overlaps in the parking slot bookings
                if (overlapCounter == 0)
                {
                    //return the parking slot id as this parking slot is available during the selected dates
                    return slot.ID;
                }
            }

            //if no available parking slots could be found, return 0
            return 0;
        }

        /// <summary>  
        /// Class for validating Google reCaptcha API response 
        /// </summary>  
        /// <param name="response">reCaptcha reponse</param>  
        /// <returns>Deserialized captcha response</returns>  
        private static CaptchaResponse ValidateCaptcha(string response)
        {
            string secret = System.Web.Configuration.WebConfigurationManager.AppSettings["recaptchaPrivateKey"];
            var client = new WebClient();
            var jsonResult = client.DownloadString(string.Format("https://www.google.com/recaptcha/api/siteverify?secret={0}&response={1}", secret, response));
            return JsonConvert.DeserializeObject<CaptchaResponse>(jsonResult.ToString());
        }

        /// <summary>
        /// HttpGet ActionResult for returning the valet view
        /// </summary>
        /// <returns>Valet view</returns>
        [Authorize]
        public ActionResult Valet()
        {
            return View();
        }

        /// <summary>
        /// HttpGet ActionResult for amending a booking
        /// </summary>
        /// <param name="id">id of booking being edited</param>
        /// <returns>Edit view with ViewBookingViewModel</returns>
        // GET: Bookings/Edit/5
        [Authorize]
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Booking booking = db.Bookings.Find(id);
            if (booking == null)
            {
                return HttpNotFound();
            }

            int dateCompareResult = DateTime.Compare(booking.Flight.DepartureDate.AddHours(-24), DateTime.Now);

            if (dateCompareResult > 0)
            {
                ViewBag.Message = "You will not be charged for any amendments to this booking.";
            }
            else if (dateCompareResult <= 0)
            {
                ViewBag.Message = "Any amendmends made to this booking will result in an admin charge to be paid on arrival.";
            }

           Vehicle vehicle = db.Vehicles.Find(booking.BookingLines.First().VehicleID);

            ViewBookingViewModel model = new ViewBookingViewModel
            {
                BookingID = booking.ID,
                DepartureDate = booking.Flight.DepartureDate,
                DepartureTime = booking.Flight.DepartureTime,
                ReturnDate = booking.Flight.ReturnDate,
                ReturnTime = booking.Flight.ReturnFlightTime,
                Duration = booking.Duration,
                Total = booking.Total,
                Valet = booking.ValetService,
                FirstName = booking.User.FirstName,
                Surname = booking.User.LastName,
                AddressLine1 = booking.User.AddressLine1,
                AddressLine2 = booking.User.AddressLine2,
                City = booking.User.City,
                Postcode = booking.User.Postcode,
                Email = booking.User.Email,
                PhoneNo = booking.User.PhoneNumber,
                VehicleMake = vehicle.Make,
                VehicleModel = vehicle.Model,
                VehicleColour = vehicle.Colour,
                VehicleRegistration = vehicle.RegistrationNumber,
                NoOfPassengers = vehicle.NoOfPassengers,
                Status = booking.BookingStatus
            };

            return View(model);
        }

        /// <summary>
        /// HttpPost ActionResult for updating the booking with new edits
        /// </summary>
        /// <param name="model">ViewBookingViewModel with inputted data</param>
        /// <returns>Booking Home View</returns>
        // POST: Bookings/Edit/5        
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public ActionResult Edit(ViewBookingViewModel model)
        {
            if (ModelState.IsValid)
            {
                //get the booking
                Booking booking = db.Bookings.Find(model.BookingID);

                //get the vehicle linked to booking
                Vehicle vehicle = db.Vehicles.Find(booking.BookingLines.First().VehicleID);

                if (booking.Flight.DepartureDate > DateTime.Now.AddHours(-24) && booking.Flight.DepartureDate <= DateTime.Now)
                {
                    //update booking
                    booking.User.FirstName = model.FirstName;
                    booking.User.LastName = model.Surname;
                    booking.User.AddressLine1 = model.AddressLine1;
                    booking.User.AddressLine2 = model.AddressLine2;
                    booking.User.City = model.City;
                    booking.User.Email = model.Email;
                    booking.User.PhoneNumber = model.PhoneNo;
                    vehicle.Make = model.VehicleMake;
                    vehicle.Model = model.VehicleModel;
                    vehicle.Colour = model.VehicleColour;
                    vehicle.RegistrationNumber = model.VehicleRegistration;
                    vehicle.NoOfPassengers = model.NoOfPassengers;

                    db.SaveChanges();

                    TempData["Success"] = "Booking Successfully Updated";

                    if (User.IsInRole("Customer"))
                    {
                        return RedirectToAction("MyBookings", "Users");
                    }
                    else
                    {
                        return RedirectToAction("Manage", "Bookings");
                    }
                }

                
            }
            return View(model);
        }

        /// <summary>
        /// HttpGet ActionResult for returning booking confirmation
        /// </summary>
        /// <param name="id">id of booking</param>
        /// <returns>Booking confirmation view</returns>
        public ActionResult Confirmation(int id)
        {
            Booking booking = db.Bookings.Find(id);
            if (booking == null)
            {
                return HttpNotFound();
            }

            int vehicleID = 0;

            //get bookingline vehicle id
            foreach (var line in db.BookingLines.ToList())
            {
                if (line.BookingID == id)
                {
                    vehicleID = line.VehicleID;
                }
            }

            Vehicle vehicle = db.Vehicles.Find(vehicleID);

            ViewBookingViewModel model = new ViewBookingViewModel
            {
                BookingID = booking.ID,
                DepartureDate = booking.Flight.DepartureDate,
                DepartureTime = booking.Flight.DepartureTime,
                ReturnDate = booking.Flight.ReturnDate,
                ReturnTime = booking.Flight.ReturnFlightTime,
                Duration = booking.Duration,
                Total = booking.Total,
                Valet = booking.ValetService,
                FirstName = booking.User.FirstName,
                Surname = booking.User.LastName,
                AddressLine1 = booking.User.AddressLine1,
                AddressLine2 = booking.User.AddressLine2,
                City = booking.User.City,
                Postcode = booking.User.Postcode,
                Email = booking.User.Email,
                PhoneNo = booking.User.PhoneNumber,
                VehicleMake = vehicle.Make,
                VehicleModel = vehicle.Model,
                VehicleColour = vehicle.Colour,
                VehicleRegistration = vehicle.RegistrationNumber,
                NoOfPassengers = vehicle.NoOfPassengers
            };


            return View(model);
        }

        /// <summary>
        /// ActionResult to convert Booking Confirmation to PDF
        /// </summary>
        /// <param name="bookingId">id of booking</param>
        /// <returns>booking confirmation view as PDF file</returns>
        public ActionResult PrintConfirmationPdf(int? bookingId)
        {
            var report = new ActionAsPdf("Confirmation", new { id = bookingId });
            return report;
        }

        /// <summary>
        /// HttpGet ActionResult to return the delete booking view
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>delete booking view</returns>
        // GET: Bookings/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Booking booking = db.Bookings.Find(id);
            if (booking == null)
            {
                return HttpNotFound();
            }
            return View(booking);
        }

        /// <summary>
        /// HttpPost ActionResult for deleting a booking
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>Index view</returns>
        // POST: Bookings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Booking booking = db.Bookings.Find(id);
            db.Bookings.Remove(booking);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        /// <summary>
        /// HttpGet ActionResult to handle if a customer chooses to purchase the valet service and update booking
        /// </summary>
        /// <param name="valetID">the id of the valet service selected</param>
        /// <returns>Payment Charge view</returns>
        [Authorize]
        public ActionResult PurchaseValet(int valetID)
        {
            Booking booking = db.Bookings.Find(TempData["bookingID"]);

            booking.ValetService = true;
            booking.Total = booking.Total + db.Tariffs.Find(valetID).Amount;

            db.SaveChanges();


            return RedirectToAction("Charge", "Payments");
        }

        /// <summary>
        /// HttpGet ActionResult to return the cancel booking view
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>Cancel view with booking parameter</returns>
        // GET: Bookings/Cancel/5
        [Authorize]
        public ActionResult Cancel(int? id)
        {
            //if the booking id is null
            if (id == null)
            {
                //return badrequest error
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            //find the booking using booking id parameter
            Booking booking = db.Bookings.Find(id);
            //if the booking returns null
            if (booking == null)
            {
                //return httpnotfound error
                return HttpNotFound();
            }

            //declare a variable to hold the result of the comparision between the current date and the booking departure date - 48 hours
            int dateCompareResult = DateTime.Compare(booking.Flight.DepartureDate.AddHours(-48), DateTime.Now);

            //if the result is more than 0
            //the cancellation is outwith 48 hours of departure date
            if (dateCompareResult > 0)
            {
                //display no cancellation charge message
                ViewBag.Message = "If you cancel this booking now, you will not be charged.";
            }
            //if the result is less than or equal to 0
            //the cancellation is within 48 hours of departure date
            else if (dateCompareResult <= 0)
            {
                //display cancellation charge message
                ViewBag.Message = "If you cancel this booking now, you will only recieve a partial refund of 70%.";
            }            
            //return the cancellation view
            return View(booking);
        }

        /// <summary>
        /// HttpPost ActionResult for cancelling a booking
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>User home</returns>
        // POST: Bookings/Cancel/5
        [HttpPost, ActionName("Cancel")]
        [ValidateAntiForgeryToken]
        [Authorize]
        public ActionResult CancelConfirmed(int id)
        {
            //declare and initialize a variable to hold a message to the user
            string message=null;

            //find the booking using the id parameter
            Booking booking = db.Bookings.Find(id);

            //declare a variable to hold the result of a date comparison between the current date and the booking departure date - 48 hours
            int dateCompareResult = DateTime.Compare(booking.Flight.DepartureDate.AddHours(-48), DateTime.Now);

            //if the departure date is before or equal to the current date
            if (booking.Flight.DepartureDate<=DateTime.Now)
            {
                //check if date compare result is more than 0 and booking is being cancelled by Customer
                //if date compare result is more than 0 - cancellation is outwith the 48 hours surplus charge period
                if (dateCompareResult > 0 && User.IsInRole("Customer"))
                {
                    //set message to the user stating they will not be charged for cancel
                    message = "Your full refund will be processed to your card or PayPal account.";
                }
                //check if date compare result is less than or equal to 0 and booking is being cancelled by Customer
                //if date compare result less than or equal to 0 - cancellation is witin the 48 hours surplus charge period
                else if (dateCompareResult <= 0 && User.IsInRole("Customer"))
                {
                    //set message to the user stating they will only receieve partial refund for cancellation
                    message = "Your partial refund will be processed to your card or PayPal account.";
                }
            }            

            //update booking status
            booking.BookingStatus = BookingStatus.Cancelled;

            //set parking slot status to available
            booking.ParkingSlot.Status = Status.Available;

            //save db changes and set success message
            db.SaveChanges();
            TempData["Success"] = "Booking No: " + id + " has been successfully cancelled." + message;
            return RedirectToAction("Index", "Users");
        }

        /// <summary>
        /// HttpGet ActionResult to return a view to allow users to check booking availability
        /// </summary>
        /// <returns>Availability View</returns>
        public ActionResult Availability()
        {
            return View();
        }

        /// <summary>
        /// HttpPost ActionResult to check the availability of a booking and return success
        /// </summary>
        /// <param name="model">AvailabilityViewModel with selected booking dates inputted</param>
        /// <returns>Availability view</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Availability(AvailabilityViewModel model)
        {
            //check view model state is valid
            if (ModelState.IsValid)
            {
                //create a TimeRange from the selected departure/return date and time in model
                TimeRange selectedTimeRange = new TimeRange(
                new DateTime(model.DepartureDate.Year, model.DepartureDate.Month, model.DepartureDate.Day, model.DepartureTime.Hours, model.DepartureTime.Minutes, 0),
                new DateTime(model.ReturnDate.Year, model.ReturnDate.Month, model.ReturnDate.Day, model.ReturnTime.Hours, model.ReturnTime.Minutes, 0));

                //call function to calculate the number of unavailable parking slots during this time period
                int unavailableSlots = GetUnavailableSlots(selectedTimeRange);

                //if the number of unavailable parking slots DOES NOT equal 150 (150 is the total number of parking spaces)
                //then a parking slot is available during this time period
                //!=1 for testing
                if (unavailableSlots != 1)
                {
                    //update availability message, store the model and return the availability view
                    TempData["Available"] = "Booking Available!";
                    TempData["AvailabilityModel"] = model;
                    ViewBag.Total = Math.Round(CalculateBookingTotal(model.DepartureDate, model.ReturnDate), 2);
                    return View(model);
                }
                //if number of unavailble slots IS equal to 150 - then parking slot is not available
                else
                {
                    //update message in TempData and return the view
                    TempData["UnAvailable"] = "Booking Not Available, Please select new booking dates.";
                    return View(model);
                }
            }
            //if model state is not valid return the availability view with model
            return View(model);
        }

        /// <summary>
        /// Function to determine how many unavailable parking slots there are for a certain TimeRange
        /// </summary>
        /// <param name="selectedTimeRange">TimeRange slots should be checked against</param>
        /// <returns>Number of unavailable parking slots</returns>
        public int GetUnavailableSlots(TimeRange selectedTimeRange)
        {
            //initialize unavailable slots to 0
            int unavailableSlots = 0;

            //loop through all parking slots
            //remove where clause for live version
            foreach (var slot in db.ParkingSlots.Where(s => s.ID == 102).ToList())
            {
                //loop through all bookings associated with parking slot
                foreach (var booking in slot.Bookings.ToList())
                {
                    //create a TimeRange variable to hold the range of dates of the booking
                    TimeRange bookingRange = new TimeRange(
                    new DateTime(
                        booking.Flight.DepartureDate.Year,
                        booking.Flight.DepartureDate.Month,
                        booking.Flight.DepartureDate.Day,
                        booking.Flight.DepartureTime.Hours,
                        booking.Flight.DepartureTime.Minutes,
                        0),
                    new DateTime(
                        booking.Flight.ReturnDate.Year,
                        booking.Flight.ReturnDate.Month,
                        booking.Flight.ReturnDate.Day,
                        booking.Flight.ReturnFlightTime.Hours,
                        booking.Flight.ReturnFlightTime.Minutes,
                        0));

                    //check if the booking time range overlaps with the user's selected booking time range
                    if (selectedTimeRange.OverlapsWith(bookingRange))
                    {
                        //if overlap - this slot is unavailable during the time range
                        //increase counter
                        unavailableSlots++;
                    }
                }
            }
            //return the number of unavailable parking slots
            return unavailableSlots;
        }

        /// <summary>
        /// ActionResult for checking in a booking
        /// </summary>
        /// <param name="id">id of booking</param>
        /// <returns>Booking Check In View</returns>
        [Authorize(Roles = "Admin, Manager, Invoice Clerk, Booking Clerk")]
        public ActionResult CheckIn(int id)
        {
            //if check in booking function returns true (success)
            if (CheckInBooking(id))
            {
                //update success message in tempdata and return departures view
                TempData["Success"] = "Booking Checked In Successfully";
                return RedirectToAction("Departures", "Users");
            }
            else
            {
                //return index view
                return RedirectToAction("Index");
            }            
        }

        /// <summary>
        /// ActionResult for checking out a booking
        /// </summary>
        /// <param name="id">id of booking</param>
        /// <returns>Booking check out view</returns>
        [Authorize(Roles = "Admin, Manager, Invoice Clerk, Booking Clerk")]
        public ActionResult CheckOut(int id)
        {
            //if check out function returns true (success)
            if (CheckOutBooking(id))
            {
                //update success message in tempdata and return user returns
                TempData["Success"] = "Booking Checked Out Successfully";
                return RedirectToAction("Returns", "Users");
            }
            else
            {
                //return index view
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// ActionResult for handling the event a customer does not show up for a booking
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>Manage bookings view</returns>
        [Authorize(Roles = "Admin, Manager, Invoice Clerk, Booking Clerk")]
        public ActionResult NoShow(int id)
        {
            try
            {
                //find the booking using the id parameter
                Booking booking = db.Bookings.Find(id);

                //update the booking status to no show
                booking.BookingStatus = BookingStatus.NoShow;

                //save database changes
                db.SaveChanges();

                //update success message in tempdata and return the manage view
                TempData["Success"] = "Booking Successfully Marked As No Show";
                return RedirectToAction("Manage");
            }
            catch (Exception ex)
            {
                //if exception occurs
                //update error message in tempdata and return the manage view
                TempData["Error"] = "Booking Could Not Be Marked As No Show";
                return RedirectToAction("Manage");
            }
        }

        /// <summary>
        /// ActionResult for handling the event a Customer is delayed returning from their trip by increasing the booking stay by 1 day
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>Manage booking view</returns>
        [Authorize(Roles = "Admin, Manager, Invoice Clerk, Booking Clerk")]
        public ActionResult Delay(int id)
        {
            try
            {
                //find the booking via id parameter
                Booking booking = db.Bookings.Find(id);

                //add 1 additonal day to the booking return date
                booking.Flight.ReturnDate.AddDays(1);
                booking.Duration++; //update booking duration
                booking.BookingStatus = BookingStatus.Delayed;  //update booking status to delayed
                booking.Total = booking.Total + booking.Tariff.Amount;  //update booking total for additional day

                //save database changes
                db.SaveChanges();

                //update success message and return to manage bookings view
                TempData["Success"] = "Booking Delay Successfully Updated";
                return RedirectToAction("Manage");
            }
            catch (Exception ex)
            {
                //if exception occurs
                //update error message in tempdata and return manage bookings view
                TempData["Error"] = "Booking Delay Could Not Be Updated";
                return RedirectToAction("Manage");
            }
        }

        /// <summary>
        /// Function for checking in a booking using booking id
        /// </summary>
        /// <param name="id">id of booking</param>
        /// <returns>true/false</returns>
        private bool CheckInBooking(int id)
        {
            //loop through all bookings
            foreach (var booking in db.Bookings.ToList())
            {
                //if the booking matches the booking id parameter
                if (booking.ID == id)
                {
                    //check booking in and update parking slot ststaus
                    booking.CheckedOut = false;
                    booking.CheckedIn = true;
                    booking.ParkingSlot.Status = Status.Occupied;
                    db.SaveChanges();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Function for checking out a booking using booking id
        /// </summary>
        /// <param name="id">booking id</param>
        /// <returns>true or false</returns>
        private bool CheckOutBooking(int id)
        {
            //loop through all bookings
            foreach (var booking in db.Bookings.ToList())
            {
                //if booking matches id parameter
                if (booking.ID == id)
                {
                    //check booking out and update parking slot status
                    booking.CheckedIn = false;
                    booking.CheckedOut = true;
                    booking.ParkingSlot.Status = Status.Available;
                    db.SaveChanges();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Function to calculate the duration of a booking using the start and end date
        /// </summary>
        /// <param name="departureDate">date of flight departure</param>
        /// <param name="returnDate">date of flight return</param>
        /// <returns>duration of booking in days</returns>
        private int CalculateBookingDuration(DateTime departureDate, DateTime returnDate)
        {
            //calculate the total duration of booking dates
            TimeSpan duration = returnDate.Subtract(departureDate);

            //return the total duration in integer number of days
            return Convert.ToInt32(duration.TotalDays);
        }

        /// <summary>
        /// Function to calculate the total cost of a booking using the start and end date
        /// </summary>
        /// <param name="departureDate">booking flight departure date</param>
        /// <param name="returnDate">booking flight return date</param>
        /// <returns>booking total</returns>
        private double CalculateBookingTotal(DateTime departureDate, DateTime returnDate)
        {
            //return the total cost of booking via the tariff price from database and booking duration
            return db.Tariffs.Find(1).Amount * Convert.ToInt32(CalculateBookingDuration(departureDate, returnDate));
        }

        /// <summary>
        /// method to unload unused resources
        /// </summary>
        /// <param name="disposing">true/false to dispose</param>
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
