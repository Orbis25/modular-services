using Commons.Helpers;
using Models.Enums.HiAccounting;
using Models.Enums.HiLoans;
using Models.Models.HiAccounting.Debs;
using Models.Models.HiLoans;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;

namespace Models.Models.HiAccounting
{
    /// <summary>
    /// Prestamo
    /// </summary>
    public class Loan : CommonsProperty
    {
        //User to Create loans
        public string UserId { get; set; }
        public User User { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        [Required(ErrorMessage = "Este campo es requerido")]
        public decimal InitialCapital { get; set; }
        [NotMapped]
        public string InitialCapitalFormated => StringHelper.FormatMoney(InitialCapital);


        [Column(TypeName = "decimal(18,2)")]
        [Required(ErrorMessage = "Este campo es requerido")]
        public decimal ActualCapital { get; set; }

        [NotMapped]
        public string ActualCapitalFormated => StringHelper.FormatMoney(ActualCapital);

        public AmortitationType AmortitationType { get; set; }
        public PaymentModality PaymentModality { get; set; }
        public RateType RateType { get; set; }

        public int Shares { get; set; }

        [Required(ErrorMessage = "Este campo es requerido")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Interest { get; set; }
        public Guid ClientUserId { get; set; }
        public ClientUser ClientUser { get; set; }
        public virtual IEnumerable<Deb> Debs { get; set; }
        public virtual IEnumerable<ReclosingHistory> ReclosingHistories { get; set; }
        public virtual IEnumerable<HistoryPaymentsLoan> HistoryPaymentsLoans { get; set; }
        public virtual IEnumerable<HistoryOnlyInterest> HistoryOnlyInterests { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountDeb { get; set; }
        public bool IsUpToDate { get; set; } = false;

        public string Note { get; set; }


        [NotMapped]
        public string SharesStr { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        [NotMapped]
        public decimal ReclosingAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        [NotMapped]
        public decimal ReclosingInitialAmount { get; set; }
        [NotMapped]
        public Guid IdLoanForReclosing { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalOfOnlyPayInterest { get; set; }
    }

}
