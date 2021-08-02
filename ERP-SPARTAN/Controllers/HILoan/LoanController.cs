using BusinesLogic.UnitOfWork;
using Commons.Extensions;
using ERP_SPARTAN.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Models.Enums;
using Models.Enums.HiAccounting;
using Models.Models.HiAccounting;
using Models.ViewModels.HiLoans.Loans;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP_SPARTAN.Controllers
{
    [Authorize(Roles = nameof(RolsAuthorization.HILoans))]
    public class LoanController : BaseController
    {
        private readonly IUnitOfWork _service;
        public LoanController(IUnitOfWork unitOfWork) => _service = unitOfWork;

        [HttpGet]
        public async Task<IActionResult> Index(FilterLoanVM model)
        {
            var userId = GetUserLoggedId();
            ViewBag.Enterprises = await _service.EnterpriseService.GetListItem(x => x.UserId == userId);
            ViewBag.Banks = await _service.BankService.GetListItem();
            model.Results = await _service.LoanService.GetAllWithRelationShip(userId, model.EnterpriseId, model.BankId);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var result = await _service.ClientUserService.GetAllWithRelationships(GetUserLoggedId(), null);
            ViewBag.Clients = result.Select(x => new SelectListItem
            {
                Text = x.User.FullName,
                Value = x.Id.ToString(),
                Group = new SelectListGroup { Name = x.Enterprise.Name }
            });
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(Loan model, string contractDate = null)
        {
            //for change the contract day
            if (!string.IsNullOrEmpty(contractDate)) model.CreateAt = contractDate.ToDateTime();

            model.UserId = GetUserLoggedId();
            var clients = await _service.ClientUserService.GetAllWithRelationships(GetUserLoggedId(), null);
            ViewBag.Clients = clients.Select(x => new SelectListItem
            {
                Text = x.User.FullName,
                Value = x.Id.ToString(),
                Group = new SelectListGroup { Name = x.Enterprise.Name }
            });



            if (!ModelState.IsValid) return View(model);

            if (model.AmortitationType == AmortitationType.Open_o_Personalfee)
            {
                model.Shares = 1;
            }
            if (model.Shares <= 0 || model.AmountDeb <= 0 || model.InitialCapital <= 0)
            {
                BasicNotification("Lo sentimos, hay campos en el formulario que deben ser mayor a cero", NotificationType.error);
                return View(model);
            }
            if (model.AmortitationType == AmortitationType.Open_o_Personalfee)
            {
                model.Shares = _service.LoanService.getShares(model);
            }

            var result = await _service.LoanService.Add(model);
            if (!result)
            {
                BasicNotification("Lo sentimos, Intente de nuevo", NotificationType.error);
                return View(model);
            }
            BasicNotification("Agregado correctamente", NotificationType.success);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetById(Guid id, State stateDeb = State.All)
        {
            ViewBag.Selected = stateDeb;
            ViewBag.Action = nameof(GetById);
            ViewBag.AccessUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/{nameof(Loan)}/{nameof(GetMyLoan)}";

            #region Have A Company Created 
            var existCompany = await _service.CompanyService.GetCompanyByUserId(GetUserLoggedId());
            if (existCompany == null) ViewBag.HaveCompany = false;
            else ViewBag.HaveCompany = true;
            #endregion

            var result = await _service.LoanService.GetByIdWithRelationships(id, stateDeb);
            if (result == null) new NotFoundView();
            return View(result);
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetMyLoan(Guid id, State stateDeb = State.All, Guid? reclosingId = null)
        {
            ViewBag.Selected = stateDeb;
            ViewBag.Action = nameof(GetMyLoan);
            ViewBag.ReclosingId = reclosingId;
            var result = await _service.LoanService.GetByIdWithRelationships(id, stateDeb);
            if (result == null) new NotFoundView();
            return View(nameof(GetById), result);
        }


        [HttpPost]
        public async Task<IActionResult> Remove(Guid id)
        {
            var result = await _service.LoanService.SoftRemove(id);
            if (!result) return BadRequest();
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> PaymentDeb(PaymentLoanVM model)
        {
            var total = Math.Abs(model.AmortizationTotal - model.Amortization);
            if (model.ExtraMount > total)
            {
                BasicNotification("El monto a abonar es mayor que el capital actual, intente abonar un monto menor", NotificationType.warning, "Error");
                return RedirectToAction(nameof(GetById), new { id = model.IdLoan });
            }
            var result = await _service.LoanService.PaymentDeb(model.IdDeb, model.IdLoan, model.ExtraMount, model.InterestOnly);
            if (!result) BasicNotification("Error intente de nuevo", NotificationType.error);
            BasicNotification("Acción Realizada", NotificationType.success);
            return RedirectToAction(nameof(GetById), new { id = model.IdLoan });
        }

        [HttpGet]
        public IActionResult GetAmortization(Loan model, string contractDate = null)
        {
            if (!string.IsNullOrEmpty(contractDate)) model.CreateAt = contractDate.ToDateTime();

            return PartialView("_GetAmortizationPartial", _service.LoanService.GetAmortization(model));
        }

        [HttpGet]
        public async Task<IActionResult> Reclosing(Guid id, State stateDeb = State.All)
        {

            ViewBag.Selected = stateDeb;
            ViewBag.Action = nameof(GetById);
            var result = await _service.LoanService.GetByIdWithRelationships(id, stateDeb);
            if (result == null) new NotFoundView();
            return View(result);
        }

        [HttpPost]
        public async Task<IActionResult> Reclosing(Loan model)
        {
            model.Id = Guid.Empty;
            model.UserId = GetUserLoggedId();
            model.InitialCapital = model.ActualCapital + model.ReclosingAmount;
            var clients = await _service.ClientUserService.GetAllWithRelationships(GetUserLoggedId(), null);
            ViewBag.Clients = clients.Select(x => new SelectListItem
            {
                Text = x.User.FullName,
                Value = x.Id.ToString(),
                Group = new SelectListGroup { Name = x.Enterprise.Name }
            });
            if (!ModelState.IsValid) return View(model);

            var result = await _service.LoanService.Add(model);
            if (!result)
            {

                BasicNotification("Lo sentimos, Intente de nuevo", NotificationType.error);
                return View(model);

            }

            result = await _service.LoanService.AddReclosing(model);
            if (!result)
            {
                BasicNotification("Lo sentimos, Intente de nuevo", NotificationType.error);
                return View(model);
            }
            BasicNotification("Reenganchado correctamente", NotificationType.success);

            return RedirectToAction(nameof(Index));
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetReclosingHistory(Guid id)
        {
            ViewBag.reclosingId = id;
            return PartialView("_GetReclosingHistoryPartial", await _service.LoanService.GetReclosing(id));
        }

        [HttpGet]
        public IActionResult GetAmortizationReclosing(Loan model)
        {
            model.ActualCapital += model.ReclosingAmount;
            return PartialView("_GetAmortizationPartial", _service.LoanService.GetAmortization(model));
        }

        [HttpGet]
        public async Task<IActionResult> OverdueInstallments() => PartialView("_OverdueInstallmentsPartial", await _service.LoanService.GetPaymentPendingClients(GetUserLoggedId()));

        [HttpGet]
        public async Task<IActionResult> GetLoanByMonth() => Ok(await _service.LoanService.GetLoanByMonth(GetUserLoggedId()));

        [HttpGet]
        public async Task<IActionResult> GetBadAndGoodPayments() => Ok(await _service.LoanService.GetBadAndGoodClientPayments(GetUserLoggedId()));

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetPaymentReceipt(Guid debId)
        {
            var result = await _service.LoanService.GetReceipt(GetUserLoggedId(), debId);
            if (result == null) return BadRequest();
            return PartialView("_GetPaymentReceiptPartial", result);
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetHistoryPaymentsLoan(Guid loanId)
        {
            var result = await _service.LoanService.GetHistoryPaymentsLoan(loanId);
            if (result == null) return BadRequest();
            return PartialView("_GetHistoryPaymentsLoan", result);
        }

        [HttpGet]
        public async Task<IActionResult> Report()
        {
            ViewBag.Enterprises = await _service.EnterpriseService.GetListItem(x => x.UserId == GetUserLoggedId());
            ViewBag.Banks = await _service.BankService.GetListItem();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetReports(FilterOfReportVM model)
        {
            ViewBag.Enterprises = await _service.EnterpriseService.GetListItem(x => x.UserId == GetUserLoggedId());
            ViewBag.Banks = await _service.BankService.GetListItem();

            if (!string.IsNullOrEmpty(model.StartDate) && !string.IsNullOrEmpty(model.EndDate))
            {
                model.Results = await _service.LoanService.GetReportOfLoot(GetUserLoggedId(), model);
                if (model.Results.Any())
                {
                    model.Banks = await _service.LoanService.GetBankResumes(GetUserLoggedId(), model);
                }
                return View(nameof(Report), model);
            }
            BasicNotification("Rango de fecha invalido, intente nuevamente", NotificationType.error, "Error");
            return RedirectToAction(nameof(Report));
        }

        [HttpPost]
        public async Task<IActionResult> IsUpToDate(Guid id)
        {
            if (id == Guid.Empty) return NotFound();
            var result = await _service.LoanService.SetOrDisableIsUpToDate(id);
            if (!result) return BadRequest();
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleIsUpToDateAll()
        {
            await _service.LoanService.ToggleAllUpToDate(GetUserLoggedId());
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> ClearAllUpToDate()
        {
            await _service.LoanService.ClearAllUpToDate(GetUserLoggedId());
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSoldOut()
        {
            var result = await _service.LoanService.GetAllSoldOut(GetUserLoggedId());
            return PartialView("_GetAllSoldOutPartial", result);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllRenclosing()
         => PartialView("_GetAllRenclosingPartial", await _service.LoanService.GetAllRenclosing(GetUserLoggedId()));

        [HttpPost]
        public async Task<IActionResult> AddNoteToDeb(Guid debId, string note)
        {
            if (debId.IsEmpty()) return BadRequest();
            await _service.LoanService.AddNoteToDeb(debId, note);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> RemoveNoteToDeb(Guid debId)
        {
            if (debId.IsEmpty()) return BadRequest();
            await _service.LoanService.RemoveNoteToDeb(debId);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> AddNoteToLoan(Guid loanId, string note)
        {
            if (loanId.IsEmpty()) return BadRequest();
            await _service.LoanService.AddNoteToLoan(loanId, note);
            return Ok();
        }
    }
}