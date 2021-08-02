using BusinesLogic.Repository.Interfaces;
using Models.Enums;
using Models.Models;
using Models.Models.HiAccounting;
using Models.Models.HiAccounting.Debs;
using Models.Models.HiLoans;
using Models.ViewModels.HiLoans.Loans;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace BusinesLogic.Interfaces.HiLoans
{
    public interface ILoanService : IBaseRepository<Loan>
    {
        Task<IEnumerable<Loan>> GetAllWithRelationShip(string userId, Guid? idEnterprise = null, Guid? BankId = null);
        Task<Loan> GetByIdWithRelationships(Guid id, State state);
        Task<bool> SoftRemove(Guid id);
        Task<bool> PaymentDeb(Guid id, Guid idLoan, decimal extraMount, bool InterestOnly);
        IEnumerable<Deb> GetAmortization(Loan model);
        Task<bool> AddReclosing(Loan model);
        int getShares(Loan model);
        Task<IEnumerable<ReclosingHistory>> GetReclosing(Guid id);
        Task<ICollection<PendingClientVM>> GetPaymentPendingClients(string createdBy);
        Task<List<MonthLoanVm>> GetLoanByMonth(string userId);
        Task<object> GetBadAndGoodClientPayments(string userId);
        Task<ReceiptVM> GetReceipt(string userId, Guid debId);
        Task<Loan> GetHistoryPaymentsLoan(Guid Loanid);
        /// <summary>
        /// Genera el reporte de las ganancias
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        Task<ICollection<ReportOfLootVM>> GetReportOfLoot(string userId, FilterOfReportVM model);
        Task<IEnumerable<BankResume>> GetBankResumes(string userId, FilterOfReportVM model);
        /// <summary>
        /// Pone o quita un prestamo al dia
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<bool> SetOrDisableIsUpToDate(Guid id);
        /// <summary>
        /// QUITA TODOS LOS PRESTAMOS QUE ESTAN AL DIA Y LO PONE COMO PENDIENTES o VISEVERSA
        /// </summary>
        /// <returns></returns>
        Task ToggleAllUpToDate(string userId);

        /// <summary>
        /// OBTIENE LOS PRESTAMOS SALDADOS
        /// </summary>
        /// <returns></returns>
        Task<ICollection<Loan>> GetAllSoldOut(string userId);
        /// <summary>
        /// OBTIENE LOS PRESAMOS QUE HAN SIDO POR REENGANCHE
        /// </summary>
        /// <returns></returns>
        Task<ICollection<Loan>> GetAllRenclosing(string userId);
        /// <summary>
        /// DESABILITA TODO LOS QUE SE MARCARON COMO "AL DIA"
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task ClearAllUpToDate(string userId);

        /// <summary>
        /// AGREGA UNA NOTA A LAS DEUDAS
        /// </summary>
        /// <param name="debId"></param>
        /// <param name="note"></param>
        /// <returns></returns>
        Task AddNoteToDeb(Guid debId, string note);
        /// <summary>
        /// ELIMINA LA NOTA DE LA DEUDA
        /// </summary>
        /// <param name="debId"></param>
        /// <param name="note"></param>
        /// <returns></returns>
        Task RemoveNoteToDeb(Guid debId);
        /// <summary>
        /// Agrega una nota al prestamo
        /// </summary>
        /// <param name="loanId"></param>
        /// <param name="note"></param>
        /// <returns></returns>
        Task AddNoteToLoan(Guid loanId, string note);

    }
}
