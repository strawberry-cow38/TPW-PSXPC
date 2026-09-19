using System;

namespace TPW.Sim
{
    /// <summary>One of the bank's four loan slots (economy.md §3 step 2, READ at 0x80088EC0).
    ///
    /// The slot fields are +0x10 months left, +0x14 remaining, +0x18 monthly payment, +0x28 an
    /// "available" flag that is 0 when the slot holds a live loan.
    ///
    /// ⚠ THE SLOT IS NOT RELEASED WHEN THE DEBT REACHES ZERO. Step 2 iterates every slot whose
    /// available flag is 0 and pays min(payment, remaining) -- it never clears the flag, so a repaid
    /// loan keeps its slot and keeps being visited, paying nothing. Modelling repayment as "the slot
    /// frees up" would let a player take a fifth loan the original would refuse.</summary>
    public sealed class Loan
    {
        /// <summary>False when this slot holds a live loan (the original's +0x28 flag, inverted for
        /// readability). A slot is Taken from the moment the loan is granted and stays Taken.</summary>
        public bool Taken { get; private set; }

        /// <summary>What is still owed. Stored in pounds in the file; Money here.</summary>
        public Money Remaining { get; private set; }

        /// <summary>Charged each month rollover, clamped by <see cref="Remaining"/>.</summary>
        public Money MonthlyPayment { get; private set; }

        /// <summary>The slot's own countdown (+0x10). Decremented every rollover while the slot is
        /// taken, INCLUDING after the debt is cleared, so it can go negative. The original does not
        /// clamp it and nothing here reads it as a termination condition -- <see cref="Remaining"/> is
        /// what actually governs payment.</summary>
        public int MonthsLeft { get; private set; }

        public void Grant(Money amount, Money monthlyPayment, int months)
        {
            Taken = true;
            Remaining = amount;
            MonthlyPayment = monthlyPayment;
            MonthsLeft = months;
        }

        /// <summary>Take this month's payment. Returns what was actually paid.</summary>
        public Money PayMonth()
        {
            if (!Taken) return Money.Zero;
            MonthsLeft--;
            Money pay = MonthlyPayment < Remaining ? MonthlyPayment : Remaining;
            Remaining -= pay;
            return pay;
        }
    }

    /// <summary>The bank's four loan slots.
    ///
    /// ⚠ FOUR, FIXED. The original has four slots and no way to grow them, so "can I borrow" is a
    /// question about free slots as well as about money.</summary>
    public sealed class LoanBook
    {
        public const int Slots = 4;

        readonly Loan[] _slots = { new Loan(), new Loan(), new Loan(), new Loan() };

        public Loan this[int i] => _slots[i];

        /// <summary>Total still owed across every slot (0x8008748C). This is what decides WHICH
        /// first-month debt message appears -- see <see cref="DebtWatch"/>.</summary>
        public Money TotalOutstanding
        {
            get
            {
                Money t = Money.Zero;
                foreach (var l in _slots) t += l.Remaining;
                return t;
            }
        }

        public bool AnyOutstanding => TotalOutstanding.Raw > 0;

        /// <summary>The first slot not yet taken, or null when all four are in use.</summary>
        public Loan FreeSlot()
        {
            foreach (var l in _slots) if (!l.Taken) return l;
            return null;
        }

        /// <summary>Pay every live loan and return the total charged.</summary>
        public Money PayMonth()
        {
            Money total = Money.Zero;
            foreach (var l in _slots) total += l.PayMonth();
            return total;
        }
    }

    /// <summary>The park's bank balance.
    ///
    /// ⚠ IT NEVER REFUSES. The bank's constructor sets a flag (BANK+8 = 1) that makes TrySpend always
    /// succeed, so the balance goes negative rather than a purchase failing. A port that blocks
    /// spending on insufficient funds is STRICTER than the original and diverges the first time a
    /// player overspends -- and the whole debt-and-bankruptcy path below only exists because the
    /// original lets it happen. See <see cref="ParkEconomy.BalanceMayGoNegative"/>.</summary>
    public sealed class Bank
    {
        public Bank(Money opening) { Balance = opening; }

        public Money Balance { get; private set; }

        /// <summary>Spend. Always succeeds; returns the new balance.</summary>
        public Money Spend(Money amount)
        {
            Balance -= amount;
            return Balance;
        }

        public Money Receive(Money amount)
        {
            Balance += amount;
            return Balance;
        }
    }

    /// <summary>What one month rollover did.</summary>
    public readonly struct MonthEndResult
    {
        public MonthEndResult(Money loans, Money wages, Money balance, DebtNotice notice)
        { LoanPayments = loans; Wages = wages; ClosingBalance = balance; Notice = notice; }

        public Money LoanPayments { get; }
        public Money Wages { get; }
        /// <summary>Loans and wages together -- the single amount actually charged.</summary>
        public Money TotalCharged => LoanPayments + Wages;
        public Money ClosingBalance { get; }
        public DebtNotice Notice { get; }
    }

    /// <summary>The month rollover: the only scheduled movement of money in the game.
    ///
    /// READ from 0x80086D70, called from 0x80066CC0 once per month change (economy.md §3). Runs every
    /// 2772-3069 ticks, i.e. 110.9-122.8 s of wall time.
    ///
    /// ⭐ THERE IS NO PER-DAY ACCOUNTING AND NO UPKEEP. Two things a reimplementation invents by
    /// reflex and the original does not have. The day rollover moves no money at all -- it only drives
    /// the weekly objectives check and guests' own timers. And step 6 computes the total value of
    /// everything built (half its purchase price) purely to RECORD it in a history array: nothing is
    /// ever charged for owning rides. Adding a daily drip or a maintenance cost would feel plausible
    /// and would be wrong.
    ///
    /// ⚠ LOANS AND WAGES ARE ONE TRANSACTION, NOT TWO. Step 4 sums them and calls TrySpend once. That
    /// is invisible while the bank is healthy and matters the moment it is not: the balance dips by the
    /// combined amount in a single step, so anything watching for a crossing sees one event of the full
    /// size rather than two smaller ones.</summary>
    public static class MonthRollover
    {
        /// <summary>Run one month end.
        ///
        /// Order matters and is the original's: loans first (step 2), then wages (step 3), then a
        /// single charge (step 4), then the debt check against the CLOSING balance.</summary>
        /// <param name="wages">Total staff wages for the month -- the caller sums
        /// <see cref="Wages.Monthly"/> over the staff, since this type does not own the roster.</param>
        public static MonthEndResult Run(Bank bank, LoanBook loans, Money wages, DebtWatch debt)
        {
            if (bank == null) throw new ArgumentNullException(nameof(bank));
            if (loans == null) throw new ArgumentNullException(nameof(loans));

            Money loanPayments = loans.PayMonth();

            // One charge, not two. See the class note.
            bank.Spend(loanPayments + wages);

            // The debt check reads the balance AFTER the charge, and asks the loan book whether
            // anything is still owed only to choose between the two first-month messages.
            DebtNotice notice = debt?.MonthEnd(bank.Balance, loans.AnyOutstanding) ?? DebtNotice.None;

            return new MonthEndResult(loanPayments, wages, bank.Balance, notice);
        }
    }
}
