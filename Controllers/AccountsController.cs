using MDB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Web.Mvc;

namespace MDB.Controllers
{
    public class AccountsController : Controller
    {
        private readonly AppDBEntities DB = new AppDBEntities();

        [HttpPost]
        public JsonResult EmailExist(string email)
        {
            return Json(DB.EmailExist(email));
        }

        #region Login and Logout
        public ActionResult Login(string message)
        {
            ViewBag.Message = message;
            return View(new LoginCredential());
        }
        [HttpPost]
        [ValidateAntiForgeryToken()]
        public ActionResult Login(LoginCredential loginCredential)
        {
            if (ModelState.IsValid)
            {
                if (DB.EmailBlocked(loginCredential.Email))
                {
                    ModelState.AddModelError("Email", "Ce compte est bloqué.");
                    return View(loginCredential);
                }
                if (!DB.EmailVerified(loginCredential.Email))
                {
                    ModelState.AddModelError("Email", "Ce courriel n'est pas vérifié.");
                    return View(loginCredential);
                }
                User user = DB.GetUser(loginCredential);
                if (user == null)
                {
                    ModelState.AddModelError("Password", "Mot de passe incorrecte.");
                    return View(loginCredential);
                }
                if (OnlineUsers.IsOnLine(user.Id))
                {
                    ModelState.AddModelError("Email", "Cet usager est déjà connecté.");
                    return View(loginCredential);
                }
                OnlineUsers.AddSessionUser(user.Id);
                Session["currentLoginId"] = DB.AddLogin(user.Id).Id;
                return RedirectToAction("Index", "Movies");
            }
            return View(loginCredential);
        }
        public ActionResult Logout()
        {
            if (Session["currentLoginId"] != null)
                DB.UpdateLogout((int)Session["currentLoginId"]);
            OnlineUsers.RemoveSessionUser();
            return RedirectToAction("Login");
        }
        #endregion
        public ActionResult Subscribe()
        {
            return View(new User());
        }
        [HttpPost]

        public ActionResult Subscribe(User user)
        {
            if(ModelState.IsValid && AppDBDAL.EmailAvailable(DB, user.Email))
            {

                AppDBDAL.AddUser(DB, user);

                SendEmailVerification(user, user.Email);

                return RedirectToAction("SubscribeDone",new {id = user.Id});
            }
            return View(user);
        }
        public JsonResult EmailAvailable(string email)
        {
            if (OnlineUsers.GetSessionUser() == null)
            {
                return Json(AppDBDAL.EmailAvailable(DB, email));
            }
            return Json(true);
        }
        public ActionResult SubscribeDone(int id)
        {
            return View(DB.FindUser(id));
        }
        public ActionResult Validate(int code,int userid)
        {
            if(AppDBDAL.VerifyUser(DB, userid, code))
            {
               return RedirectToAction("VerifyDone", new {id = userid}); //succes
            }
            return RedirectToAction("VerifyFail"); //fail
        }
        public ActionResult VerifyDone(int id)
        {
            return View(DB.FindUser(id));
        }
        public ActionResult VerifyFail()
        {
            return View();
        }
        public ActionResult ResetPasswordCommand()
        {
            return View(new EmailView());
        }
        [HttpPost]
        public ActionResult ResetPasswordCommand(EmailView mail)
        {
            SendPasswordCode(mail.Email);

            return RedirectToAction("ResetSent");
        }
        public ActionResult ResetSent()
        {
            return View();
        }
        public ActionResult ResetSentFail()
        {
            return View();
        }
        public ActionResult Profil()
        {
            User user = OnlineUsers.GetSessionUser();
            if (user != null)
            {
                Session["guid"] = Guid.NewGuid().ToString();
                Session["userPassword"] = user.Password;
                Session["userEmail"] = user.Email;
                return View(user);
            }
            return RedirectToAction("Login");
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Profil(User user)
        {
            if(ModelState.IsValid)
            {
                User oldUser = OnlineUsers.GetSessionUser();
                if ((string)Session["guid"] == user.Password)
                {
                    user.Password = user.ConfirmPassword = (string)Session["userPassword"];

                }
                if ((string) Session["userEmail"] != user.Email && DB.EmailExist(user.Email)) {
                    ModelState.AddModelError("Email", "Ce courriel n'est pas disponible");
                    return View(user);
                }
                else if ((string)Session["userEmail"] != user.Email)
                {
                    DB.Add_UnverifiedEmail(user.Id, user.Email);

                    SendEmailVerification(user, user.Email);

                    user.Email = oldUser.Email;
                }
                DB.UpdateUser(user);
                return RedirectToAction("Index", "Movies");
            }
            return RedirectToAction("login");
        }
        public ActionResult ResetPassword(int code, int userid) {
            ResetPasswordCommand pass = AppDBDAL.FindResetPasswordCommand(DB, userid, code);

            if (pass != null)
            {

                return View(AppDBDAL.FindUser(DB, userid));
            }
            return RedirectToAction("ResetSentFail");
            
        }
        [HttpPost]
        public ActionResult ResetPassword(User user) {
            if(DB.ResetPassword(user.Id, user.Password))
            {
                return RedirectToAction("ResetSucces");
            }
            return RedirectToAction("ResetFail");
        }
        public ActionResult ResetSucces() {
            return View();
        }
        public ActionResult ResetFail() {
            return View();
        }

        void SendEmailVerification(User user, string newEmail)
        {
            if (user.Id != 0)
            {
                UnverifiedEmail unverifiedEmail = DB.Add_UnverifiedEmail(user.Id, newEmail);
                if (unverifiedEmail != null)
                {
                    string verificationUrl = Url.Action("Validate", "Accounts", null, Request.Url.Scheme);
                    String Link = @"<br/><a href='" + verificationUrl + "?userid=" + user.Id + "&code=" + unverifiedEmail.VerificationCode + @"' > Confirmez votre inscription...</a>";

                    String suffixe = "";
                    if (user.GenderId == 2)
                    {
                        suffixe = "e";
                    }
                    string Subject = "MDB - Vérification d'inscription...";

                    string Body = "Bonjour " + user.GetFullName(true) + @",<br/><br/>";
                    Body += @"Merci de vous être inscrit" + suffixe + " au site MDB. <br/>";
                    Body += @"Pour utiliser votre compte vous devez confirmer votre inscription en cliquant sur le lien suivant : <br/>";
                    Body += Link;
                    Body += @"<br/><br/>Ce courriel a été généré automatiquement, veuillez ne pas y répondre.";
                    Body += @"<br/><br/>Si vous éprouvez des difficultés ou s'il s'agit d'une erreur, veuillez le signaler à <a href='mailto:"
                         + SMTP.OwnerEmail + "'>" + SMTP.OwnerName + "</a> (Webmestre du site MDB)";

                    SMTP.SendEmail(user.GetFullName(), unverifiedEmail.Email, Subject, Body);
                }
            }
        }
        void SendPasswordCode(string email)
        {
           
                ResetPasswordCommand unverifiedEmail = AppDBDAL.AddResetPasswordCommand(DB,email);
                if (unverifiedEmail != null)
                {
                    string verificationUrl = Url.Action("ResetPassword", "Accounts", null, Request.Url.Scheme);
                    String Link = @"<br/><a href='" + verificationUrl + "?code=" + unverifiedEmail.VerificationCode + "&userid=" + unverifiedEmail.UserId + @"' > Renitialiser votre mot de passe...</a>";

                    string Subject = "MDB - Renitialisation du mot de passe...";

                    string Body = "Bonjour<br/><br/>";
                Body += @"Pour renitialiser votre mot de passe vous devez cliquer sur le lien suivant : <br/>";
                Body += Link;
                    Body += @"<br/><br/>Ce courriel a été généré automatiquement, veuillez ne pas y répondre.";
                    Body += @"<br/><br/>Si vous éprouvez des difficultés ou s'il s'agit d'une erreur, veuillez le signaler à <a href='mailto:"
                         + SMTP.OwnerEmail + "'>" + SMTP.OwnerName + "</a> (Webmestre du site MDB)";

                    SMTP.SendEmail("Utilisateur",email, Subject, Body);
                }
        }
    }
}