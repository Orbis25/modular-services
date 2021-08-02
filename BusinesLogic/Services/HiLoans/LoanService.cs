using BusinesLogic.Interfaces.HiLoans;
using BusinesLogic.Repository.Services;
using Commons.Extensions;
using Microsoft.EntityFrameworkCore;
using Models.Contexts;
using Models.Enums;
using Models.Enums.HiAccounting;
using Models.Enums.HiLoans;
using Models.Models;
using Models.Models.HiAccounting;
using Models.Models.HiAccounting.Debs;
using Models.Models.HiLoans;
using Models.ViewModels.HiLoans.Loans;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace BusinesLogic.Services.HiLoans
{
    public class LoanService : BaseRepository<Loan>, ILoanService
    {
        public readonly ApplicationDbContext _dbContext;
        public LoanService(ApplicationDbContext dbContext) : base(dbContext)
            => _dbContext = dbContext;

        public override async Task<bool> Add(Loan model)
        {
            model.ActualCapital = model.InitialCapital;
            if (model.ReclosingInitialAmount > 0) model.InitialCapital = model.ReclosingInitialAmount;
            _dbContext.Loans.Add(model);
            var result = await _dbContext.SaveChangesAsync() > 0;
            if (result) await AddDebs(model, model.CreateAt);
            return result;
        }
        public async Task<IEnumerable<Loan>> GetAllWithRelationShip(string userId, Guid? idEnterprise = null, Guid? bankId = null)
        {
            var result = Filter(x => x.UserId == userId && x.State == State.Active && x.ActualCapital > 0)
                .Include(x => x.Debs)
                .Include(x => x.ClientUser.Enterprise)
                .Include(x => x.ReclosingHistories)
                .Include(x => x.ClientUser)
                .ThenInclude(x => x.User)
            .AsQueryable();

            if (idEnterprise != null) result = result.Where(x => x.ClientUser.EnterpriseId == idEnterprise);
            if (bankId != null) result = result.Where(x => x.ClientUser.BankId == bankId);

            return await result.OrderByDescending(x => x.CreateAt).ToListAsync();
        }
        public async Task<Loan> GetByIdWithRelationships(Guid id, State state)
        {
            var result = new Loan { };
            result = await GetAll()
                .Include(x => x.ReclosingHistories)
                .Include(x => x.ClientUser)
                .ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.Id == id);

            if (state == State.All) result.Debs = await _dbContext.Debs.Where(x => x.LoanId == id).ToListAsync();
            else result.Debs = await _dbContext.Debs.Where(x => x.LoanId == id && x.State == state).ToListAsync();

            var pendingsDebs = await _dbContext.Debs.CountAsync(x => x.State == State.Active && x.LoanId == id);
            var paymentDebs = await _dbContext.Debs.CountAsync(x => x.State == State.Payment && x.LoanId == id);
            result.SharesStr = pendingsDebs.ToString();
            return result;
        }
        public async Task<bool> PaymentDeb(Guid id, Guid idLoan, decimal extraMount, bool interestOnly)
        {
            var deb = await _dbContext.Debs.FirstOrDefaultAsync(x => x.Id == id);
            var loan = await _dbContext.Loans.FirstOrDefaultAsync(x => x.Id == idLoan);
            decimal actualCapital = deb.State == State.Active ? loan.ActualCapital - (decimal)deb.Amortitation : loan.ActualCapital + (decimal)deb.Amortitation;

            if (!interestOnly)
            {
                loan.ActualCapital = actualCapital;

            }
            _dbContext.Update(loan);
            await CommitAsync();
            var result = false;

            if (interestOnly)
            {
                _dbContext.HistoryOnlyInterests.Add(new HistoryOnlyInterest { Amount = deb.Interest, LoanId = idLoan });
                await CommitAsync();
            }

            if (interestOnly && deb.State != State.Payment)
            {
                deb.AllowPayInterest = interestOnly;

                _dbContext.HistoryPaymentsLoan.Add(new HistoryPaymentsLoan { Share = deb.Share, IdLoan = deb.LoanId, ToPay = deb.ToPay, ExtraMount = 0, EndBalance = deb.EndBalance, State = State.OnlyInterest });

                _dbContext.Debs.Update(deb);

                if (await _dbContext.SaveChangesAsync() > 0)
                {
                    await RecalculateDebs(idLoan, 0, false, interestOnly);
                }
            }
            else
            {
                if (deb.State == State.Active)
                {
                    deb.ExtraMount = extraMount;
                    deb.IsExtraMount = extraMount > 0;
                    deb.State = State.Payment;
                    deb.AllowPayInterest = false;
                    decimal endbalance = deb.EndBalance - extraMount;
                    _dbContext.HistoryPaymentsLoan.Add(new HistoryPaymentsLoan { Share = deb.Share, IdLoan = deb.LoanId, ToPay = deb.ToPay, ExtraMount = extraMount, EndBalance = endbalance, State = State.Payment });

                    _dbContext.Debs.Update(deb);
                    await CommitAsync();
                }
                else
                {
                    var extra = deb.ExtraMount;
                    deb.ExtraMount = 0;
                    deb.IsExtraMount = false;
                    deb.State = State.Active;
                    _dbContext.HistoryPaymentsLoan.Add(new HistoryPaymentsLoan { Share = deb.Share, IdLoan = deb.LoanId, ToPay = deb.ToPay, ExtraMount = extraMount, EndBalance = deb.EndBalance, State = State.Cancelled });
                    _dbContext.Debs.Update(deb);

                    if (await CommitAsync())
                    {
                        if (extra > 0) await RecalculateDebs(idLoan, extra, false);
                    }

                }
            }


            if (extraMount > 0)
            {
                _dbContext.Debs.Update(deb);
                if (await CommitAsync())
                {
                    result = await RecalculateDebs(idLoan, extraMount);
                }
            }
            return result;
        }
        public async Task<bool> SoftRemove(Guid id)
        {
            var model = await GetById(id);
            model.State = State.Removed;
            model.UpdateAt = DateTime.Now;
            var result = await Update(model);
            if (result)
            {
                var reenclosingHistory = await _dbContext.ReclosingHistories.Where(x => x.IdLoan == id).ToListAsync();
                if (reenclosingHistory.Any())
                {
                    foreach (var item in reenclosingHistory)
                    {

                        var loan = await _dbContext.Loans.FirstOrDefaultAsync(x => x.Id == item.IdRenclosingLoan);
                        if (loan != null)
                        {
                            _dbContext.Remove(loan);
                            result = await _dbContext.SaveChangesAsync() > 0;
                        }

                    }
                }
            }
            return result;
        }
        private IEnumerable<Deb> OpenfeeDebs(Loan loan, DateTime lastDateTime, int count = 0, bool interestonly = false)
        {
            double interest = (double)(loan.Interest) / 100;
            double monthly = interest;

            // double shares = (loan.Shares - count);
            decimal cuotasInformal = loan.ActualCapital / loan.AmountDeb;


            bool interestOnly = interestonly;
            //decimal LoanDebsamortitation = 0;
            if (loan.Debs != null)
            {
                //if (loan.Debs.Any()) LoanDebsamortitation = (decimal)loan.Debs.FirstOrDefault().Amortitation;
                // if (loan.Debs.Any()) cuotasInformal = cuotasInformal- loan.Debs.FirstOrDefault().Share;
            }
            decimal decimas = cuotasInformal - Math.Truncate(cuotasInformal);
            double shares = (double)Math.Truncate(cuotasInformal);
            shares = decimas > 0 ? shares += 1.0 : shares;
            decimal balance = loan.ActualCapital;

            if (loan.RateType == RateType.Anual) monthly = interest / 12;
            var payment = (double)loan.AmountDeb;
            var result = new List<Deb>();
            var nextPayment = lastDateTime;
            int debNumber = count;
            int debNumberLogic = debNumber;

            for (int i = 0; i < shares; i++)
            {
                debNumber++;

                // int tesst = !interestonly?debNumberLogic+1:debNumberLogic;
                if (debNumber > 1)
                {

                    if ((i == (shares - 1)) && decimas > 0)
                    {
                        payment = (double)decimas * (double)loan.AmountDeb;
                    }
                }

                debNumberLogic = debNumber;
                var interestDeb = (double)balance * monthly;

                var deb = new Deb { };
                if (interestOnly) { deb.AllowPayInterest = interestOnly; interestOnly = false; }
                deb.Share = debNumber;
                nextPayment = GetDateOfPayment(loan.PaymentModality, nextPayment);
                deb.DateOfPayment = nextPayment;
                deb.Amount = balance;
                deb.Interest = (decimal)interestDeb;

                deb.Amortitation = payment;
                deb.EndBalance = balance - (decimal)deb.Amortitation;
                deb.ToPay = payment + interestDeb;
                deb.LoanId = loan.Id;
                balance -= (decimal)deb.Amortitation;

                result.Add(deb);
            }
            return result;
        }
        private IEnumerable<Deb> FixedfeeDebs(Loan loan, DateTime lastDateTime, int count = 0, bool interestonly = false)
        {
            double interest = (double)(loan.Interest) / 100;
            double monthly = interest;

            double shares = (loan.Shares - count);
            bool interestOnly = interestonly;

            decimal balance = loan.ActualCapital;
            if (loan.RateType == RateType.Anual) monthly = interest / 12;
            var payment = (double)loan.ActualCapital * (monthly / (1 - Math.Pow(1 + monthly, -shares)));
            var result = new List<Deb>();
            var nextPayment = lastDateTime;
            int debNumber = count;
            for (int i = 0; i < shares; i++)
            {
                debNumber++;
                var interestDeb = balance * (decimal)monthly;

                var deb = new Deb { };
                if (interestOnly) { deb.AllowPayInterest = interestOnly; interestOnly = false; }
                deb.Share = debNumber;
                nextPayment = GetDateOfPayment(loan.PaymentModality, nextPayment);
                deb.DateOfPayment = nextPayment;
                deb.Amount = balance;
                deb.Interest = interestDeb;
                deb.Amortitation = payment - (double)interestDeb;
                deb.EndBalance = balance - (decimal)deb.Amortitation;
                deb.ToPay = payment;
                deb.LoanId = loan.Id;
                balance -= (decimal)deb.Amortitation;
                result.Add(deb);
            }
            return result;
        }
        private IEnumerable<Deb> FixedInterestDebs(Loan loan, DateTime lastDateTime, int count = 0, bool interestonly = false)
        {
            double interest = (double)((decimal)loan.Interest) / 100;
            double monthly = interest;
            int shares = loan.Shares - count;
            bool interestOnly = interestonly;
            decimal LoanDebsamortitation = 0;
            decimal interestValue = 0;
            if (loan.Debs != null)
            {
                if (loan.Debs.Any())
                {
                    LoanDebsamortitation = (decimal)loan.Debs.FirstOrDefault().Amortitation;
                    interestValue = loan.Debs.FirstOrDefault().Interest;
                }
            }
            decimal balanceFixed = loan.ActualCapital;
            decimal balance = loan.ActualCapital;
            if (loan.RateType == RateType.Anual) monthly = interest / 12;
            var result = new List<Deb>();

            var nextPayment = lastDateTime;
            int debNumber = count;
            for (int i = 0; i < shares; i++)
            {
                debNumber++;
                //    var interestdeb = balanceFixed * (decimal)monthly;
                var interestdeb = interestValue == 0 ? (balanceFixed * (decimal)monthly) : interestValue;
                var monthlyPrincipal = balanceFixed / shares;
                nextPayment = GetDateOfPayment(loan.PaymentModality, nextPayment);
                double payment = (double)(monthlyPrincipal + interestdeb);
                var deb = new Deb
                {
                    Share = debNumber,
                    DateOfPayment = nextPayment,
                    Amount = balance,
                    Interest = interestdeb,
                    Amortitation = (double)monthlyPrincipal,
                    ToPay = payment,
                    EndBalance = balance - monthlyPrincipal,
                    LoanId = loan.Id
                };
                if (interestOnly) { deb.AllowPayInterest = interestOnly; interestOnly = false; }
                balance -= monthlyPrincipal;
                result.Add(deb);
            }
            return result;
        }
        private IEnumerable<Deb> CapitalEndDebs(Loan loan, DateTime lastDateTime, int count = 0, bool interestonly = false)
        {
            double interest = (double)((decimal)loan.Interest) / 100;
            double monthly = interest;
            int shares = loan.Shares - count;
            bool interestOnly = interestonly;
            decimal LoanDebsamortitation = 0;

            decimal interestValue = 0;
            if (loan.Debs != null)
            {
                if (loan.Debs.Any())
                {
                    LoanDebsamortitation = (decimal)loan.Debs.FirstOrDefault().Amortitation;
                    interestValue = loan.Debs.FirstOrDefault().Interest;
                }
            }

            decimal balance = loan.ActualCapital - LoanDebsamortitation;
            if (loan.RateType == RateType.Anual) monthly = interest / 12;
            var result = new List<Deb>();
            var nextPayment = lastDateTime;
            int debNumber = count;
            for (int i = 0; i < shares; i++)
            {
                debNumber++;
                var interestdeb = interestValue == 0 ? (balance * (decimal)monthly) : interestValue;
                var payment = interestdeb;
                decimal monthlyPrincipal = 0;
                nextPayment = GetDateOfPayment(loan.PaymentModality, nextPayment);


                if (i == (shares - 1))
                {
                    monthlyPrincipal = balance;
                    payment = balance + interestdeb;
                }

                var deb = new Deb
                {
                    Share = debNumber,
                    DateOfPayment = nextPayment,
                    Interest = interestdeb,
                    Amount = balance,
                    Amortitation = (double)monthlyPrincipal,
                    EndBalance = balance - monthlyPrincipal,
                    ToPay = (double)payment,
                    LoanId = loan.Id
                };
                if (interestOnly) { deb.AllowPayInterest = interestOnly; interestOnly = false; }
                result.Add(deb);
            }
            return result;
        }
        private DateTime GetDateOfPayment(PaymentModality modality, DateTime dateNow)
        {
            var date = dateNow;
            switch (modality)
            {
                case PaymentModality.Daily:
                    date = date.AddDays(1);
                    return date;
                case PaymentModality.Week:
                    date = date.AddDays(7);
                    return date;
                case PaymentModality.Biweekly:
                    date = date.AddDays(15);
                    return date;
                case PaymentModality.Month:
                    date = date.AddMonths(1);
                    return date;

                default: return date.AddYears(1);
            }
        }
        private async Task<bool> AddDebs(Loan model, DateTime lastDateTime, int count = 0, bool interestOnly = false)
        {
            IEnumerable<Deb> debs;
            if (model.AmortitationType == AmortitationType.Open_o_Personalfee)
            {
                debs = OpenfeeDebs(model, lastDateTime, count, interestOnly);
            }
            else if (model.AmortitationType == AmortitationType.Fixedfee)
            {
                debs = FixedfeeDebs(model, lastDateTime, count, interestOnly);
            }
            else if (model.AmortitationType == AmortitationType.FixedInterest)
            {
                debs = FixedInterestDebs(model, lastDateTime, count, interestOnly);
            }
            else debs = CapitalEndDebs(model, lastDateTime, count, interestOnly);

            _dbContext.Debs.AddRange(debs);
            return await CommitAsync();
        }
        private async Task<bool> RemoveAllDebs(IEnumerable<Deb> debs)
        {
            _dbContext.Debs.RemoveRange(debs);
            return await CommitAsync();
        }
        private async Task<bool> RecalculateDebs(Guid idLoan, decimal extraMount, bool isDiscount = true, bool interestOnly = false)
        {
            var result = false;
            var debs = await _dbContext.Debs.Where(x => x.State == State.Active && x.LoanId == idLoan).ToListAsync();
            var count = await _dbContext.Debs.CountAsync(x => x.State == State.Payment && x.LoanId == idLoan);
            var lastPayment = await _dbContext.Debs.Where(x => x.State == State.Payment && x.LoanId == idLoan)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();
            var actualPayment = await _dbContext.Debs.Where(x => x.State == State.Active && x.AllowPayInterest && x.LoanId == idLoan)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

            if (await RemoveAllDebs(debs))
            {
                var loan = await GetById(idLoan);
                if (!interestOnly)
                {
                    loan.ActualCapital = isDiscount ? loan.ActualCapital - extraMount : loan.ActualCapital + extraMount;
                }
                _dbContext.Loans.Update(loan);

                if (await CommitAsync())
                {
                    var dateOfPayment = DateTime.Now;
                    if (lastPayment != null) dateOfPayment = lastPayment.DateOfPayment;
                    if (actualPayment != null) dateOfPayment = actualPayment.AllowPayInterest ? actualPayment.DateOfPayment : dateOfPayment;
                    if (await AddDebs(loan, dateOfPayment, count, interestOnly)) result = true;
                    else result = false;
                }
            }
            return result;
        }
        public IEnumerable<Deb> GetAmortization(Loan model)
        {
            IEnumerable<Deb> debs;
            if (model.AmortitationType == AmortitationType.Open_o_Personalfee)
            {
                debs = OpenfeeDebs(model, model.CreateAt);
            }
            else if (model.AmortitationType == AmortitationType.Fixedfee)
            {
                debs = FixedfeeDebs(model, model.CreateAt);
            }
            else if (model.AmortitationType == AmortitationType.FixedInterest)
            {
                debs = FixedInterestDebs(model, model.CreateAt);
            }
            else debs = CapitalEndDebs(model, model.CreateAt);
            return debs;
        }

        public async Task<bool> AddReclosing(Loan model)
        {
            var loan = await _dbContext.Loans.FirstOrDefaultAsync(x => x.Id == model.IdLoanForReclosing);
            loan.State = State.Reclosing;
            _dbContext.Update(loan);
            var result = await _dbContext.SaveChangesAsync() > 0;

            if (result)
            {
                var reclosingHistory = _dbContext.ReclosingHistories.Where(x => x.IdLoan == model.IdLoanForReclosing);
                if (reclosingHistory.Any())
                {
                    await reclosingHistory.ForEachAsync((x) =>
                    {
                        x.IdLoan = model.Id;
                    });

                    _dbContext.ReclosingHistories.UpdateRange(reclosingHistory);
                    result = await _dbContext.SaveChangesAsync() > 0;
                }

                if (result)
                {
                    _dbContext.ReclosingHistories.Add(new ReclosingHistory { IdLoan = model.Id, Amount = model.ReclosingAmount, IdRenclosingLoan = model.IdLoanForReclosing });
                    result = await _dbContext.SaveChangesAsync() > 0;
                }
            }
            return result;
        }
        public int getShares(Loan model)
        {

            decimal cuotasInformal = model.InitialCapital / model.AmountDeb;
            decimal decimas = cuotasInformal - Math.Truncate(cuotasInformal);
            double shares = (double)Math.Truncate(cuotasInformal);
            shares = decimas > 0 ? shares += 1.0 : shares;
            return (int)shares;
        }
        public async Task<IEnumerable<ReclosingHistory>> GetReclosing(Guid id)
            => await _dbContext.ReclosingHistories.Where(x => x.IdLoan == id).ToListAsync();
        public async Task<ICollection<PendingClientVM>> GetPaymentPendingClients(string createdBy)
        {
            var result = _dbContext.Loans.AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Debs)
                .Include(x => x.ClientUser)
                .ThenInclude(x => x.Enterprise)
                .Include(x => x.ClientUser.Enterprise)
                .Where(x => x.UserId == createdBy
                && x.State == State.Active
                && x.ClientUser.State == State.Active
                && x.Debs.Any(x => x.State == State.Active && x.DateOfPayment < DateTime.Now))
                .Select(x => new PendingClientVM
                {
                    Name = x.ClientUser.User.Name,
                    LastName = x.ClientUser.User.LastName,
                    LoanId = x.Id,
                    UserId = x.ClientUser.User.Id,
                    EnterpriseName = x.ClientUser.Enterprise.Name,
                    UserName = x.ClientUser.User.UserName,
                    AmountLoan = x.ActualCapitalFormated,
                    TotalRate = x.Debs.Count(x => x.State == State.Active && x.DateOfPayment < DateTime.Now)
                }); ;
            return await result.ToListAsync();
        }
        public async Task<List<MonthLoanVm>> GetLoanByMonth(string userId)
        {
            CultureInfo culture = new CultureInfo("es");
            return await GetAll().Where(x => x.UserId == userId && x.State == State.Active)
                .GroupBy(x => x.CreateAt.Month)
                .Select(x =>
                new MonthLoanVm
                {
                    Month = culture.DateTimeFormat.GetMonthName(x.Key),
                    Quantity = x.Count()
                }).ToListAsync();
        }
        public async Task<object> GetBadAndGoodClientPayments(string userId)
        {
            var result = _dbContext.Loans.Include(x => x.User)
               .Include(x => x.Debs)
               .Include(x => x.ClientUser)
               .Where(x => x.UserId == userId && x.State == State.Active
               && x.ClientUser.State == State.Active);

            if (result.Any())
            {
                return new
                {
                    Bad = await result.CountAsync(x => x.Debs.Any(x => x.State == State.Active && x.DateOfPayment < DateTime.Now)),
                    Good = await result.CountAsync(x => x.Debs.Any(x => x.State == State.Payment))
                };
            }
            return null;
        }
        public async Task<ReceiptVM> GetReceipt(string userId, Guid debId)
        {
            var company = await _dbContext.Companies.FirstOrDefaultAsync(x => x.UserId == userId);
            var deb = await _dbContext.Debs.Include(x => x.Loan).ThenInclude(x => x.ClientUser)
                .ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.Id == debId);

            decimal amount = 0;
            var isExtraMount = false;
            var isInteres = false;
            if (deb.IsExtraMount)
            {
                amount = deb.ExtraMount + (decimal)deb.ToPay;
                isExtraMount = true;
            }
            else if (deb.AllowPayInterest)
            {
                amount = deb.Interest;
                isInteres = true;
            }
            else amount = (decimal)deb.ToPay;

            return new ReceiptVM
            {
                CompanyName = company.Name,
                Address = company.Address,
                PhoneNumber = company.PhoneNumber,
                Rnc = company.Rnc,
                Amount = Math.Round(amount, 2),
                ExtraAmount = Math.Round(deb.ExtraMount, 2),
                Interes = Math.Round(deb.Interest, 2),
                IsExtraAmount = isExtraMount,
                OnlyInteres = isInteres,
                ActualCapital = Math.Round(deb.Amount - (decimal)deb.Amortitation, 2),
                ToPay = (decimal)Math.Round(deb.ToPay, 2),
                Deb = deb
            };
        }
        public async Task<Loan> GetHistoryPaymentsLoan(Guid Loanid)
        {
            var result = await _dbContext.Loans.Where(x => x.Id == Loanid).Include(x => x.HistoryPaymentsLoans).FirstAsync();
            var resultHistoryPaymentsLoans = await _dbContext.HistoryPaymentsLoan.Where(x => x.IdLoan == Loanid).ToListAsync();
            var pendingsDebs = await _dbContext.Debs.CountAsync(x => x.State == State.Active && x.LoanId == Loanid);
            result.HistoryPaymentsLoans = resultHistoryPaymentsLoans;
            result.SharesStr = pendingsDebs.ToString();

            return result;
        }
        public async Task<ICollection<ReportOfLootVM>> GetReportOfLoot(string userId, FilterOfReportVM model)
        {
            var results = Filter(x => x.UserId == userId
              && x.State != State.Reclosing
              && x.State == State.Active
              && (x.CreateAt >= model.StartDate.ToDateTime() && x.CreateAt <= model.EndDate.ToDateTime())
              && x.Debs.Where(x => x.State == model.DebState || x.AllowPayInterest).Any())
                .Include(x => x.Debs)
                .Include(x => x.HistoryOnlyInterests)
                .Include(x => x.ClientUser);

            if (model.Bank != null)
                results = results.ThenInclude(x => x.Bank).Where(x => x.ClientUser.BankId == model.Bank).Include(x => x.ClientUser);
            if (model.Enterprise != null)
                results = results.ThenInclude(x => x.Bank).Where(x => x.ClientUser.EnterpriseId == model.Enterprise).Include(x => x.ClientUser);

            return await results.Select(x => new ReportOfLootVM
            {
                CompanyName = x.ClientUser.Enterprise.Name,
                Date = x.CreatedAtStr,
                OnlyInterest = x.HistoryOnlyInterests.Sum(x => x.Amount),
                Debs = x.Debs.Where(x => x.State == model.DebState).Sum(_ => _.Interest),
                BankName = x.ClientUser.Bank.Name,
            }).ToListAsync();
        }
        public async Task<IEnumerable<BankResume>> GetBankResumes(string userId, FilterOfReportVM model)
        {
            var results = Filter(x => x.UserId == userId
            && x.State != State.Reclosing
            && x.State == State.Active
            && (x.CreateAt >= model.StartDate.ToDateTime() && x.CreateAt <= model.EndDate.ToDateTime())
            &&  x.Debs.Where(_ => _.State == model.DebState || _.AllowPayInterest).Any())
                .Include(x => x.Debs)
                .Include(x => x.HistoryOnlyInterests)
                .Include(x => x.ClientUser);

            if (model.Bank != null)
                results = results.ThenInclude(x => x.Bank).Where(x => x.ClientUser.BankId == model.Bank).Include(x => x.ClientUser);
            if (model.Enterprise != null)
                results = results.ThenInclude(x => x.Bank).Where(x => x.ClientUser.EnterpriseId == model.Enterprise).Include(x => x.ClientUser);

            var result = await results.AsNoTracking().Select(x =>
             new BankResume
             {
                 Payments = x.Debs.Where(_ => _.State == model.DebState).Sum(x => x.Interest),
                 Interest = x.HistoryOnlyInterests.Sum(x => x.Amount),
                 BankName = x.ClientUser.Bank.Name
             }).ToListAsync();


            var total = result.GroupBy(x => x.BankName, (t, s) => new BankResume
            {
                Interest = s.Select(x => x.Interest).Sum(x => x),
                Payments = s.Select(x => x.Payments).Sum(x => x),
                BankName = t
            });

            return total;

        }
        public async Task<bool> SetOrDisableIsUpToDate(Guid id)
        {
            var result = await GetById(id);
            result.IsUpToDate = !result.IsUpToDate;
            return await Update(result);
        }
        public async Task ToggleAllUpToDate(string userId)
        {
            var results = Filter(x => x.UserId == userId);
            if (results.Any(x => !x.IsUpToDate)) await results.ForEachAsync(x => x.IsUpToDate = true);
            else await results.ForEachAsync(x => x.IsUpToDate = false);
            _dbContext.UpdateRange(results);
            await CommitAsync();
        }
        public async Task ClearAllUpToDate(string userId)
        {
            var results = Filter(x => x.UserId == userId);
            await results.ForEachAsync(x => x.IsUpToDate = false);
            _dbContext.UpdateRange(results);
            await CommitAsync();
        }
        public async Task<ICollection<Loan>> GetAllSoldOut(string userId)
            => await Filter(x => x.ActualCapital <= 0 && x.UserId == userId).Include(x => x.ClientUser.User)
            .Include(x => x.ClientUser).ThenInclude(x => x.Enterprise).ToListAsync();
        public async Task<ICollection<Loan>> GetAllRenclosing(string userId)
            => await Filter(x => x.State == State.Reclosing && x.UserId == userId).Include(x => x.ClientUser.User)
            .Include(x => x.ClientUser).ThenInclude(x => x.Enterprise).ToListAsync();

        public async Task AddNoteToDeb(Guid debId, string note)
        {
            var deb = await _dbContext.Debs.FirstOrDefaultAsync(x => x.Id == debId);
            deb.Note = note;
             _dbContext.Debs.Update(deb);
            await CommitAsync();
        }

        public async Task RemoveNoteToDeb(Guid debId)
        {
            var deb = await _dbContext.Debs.FirstOrDefaultAsync(x => x.Id == debId);
            deb.Note = null;
            _dbContext.Debs.Update(deb);
            await CommitAsync();
        }

        public async Task AddNoteToLoan(Guid loanId, string note)
        {
            var loan = await GetById(loanId);
            loan.Note = note;
            await Update(loan);
        }
    }
}
