﻿using FOAEA3.Model;
using FOAEA3.Model.Enums;
using FOAEA3.Model.Interfaces.Repository;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace FOAEA3.Business.Areas.Financials
{
    public class TransactionManager
    {
        private IRepositories DB { get; }
        private IRepositories_Finance DBfinance { get; }

        public TransactionManager(IRepositories db, IRepositories_Finance dbFinance)
        {
            DB = db;
            DBfinance = dbFinance;
        }

        public async Task<TransactionResult> CreateFaFrDe(string transactionType, SummFAFR_DE_Data faTransaction)
        {
            var result = await ValidateTransaction(transactionType, faTransaction);

            ControlBatchData batch;
            SummFAFR_DE_Data newTransaction;

            if (result.ReturnCode != ReturnCode.Invalid)
                (newTransaction, batch) = await BuildFA(transactionType, faTransaction);
            else
                (newTransaction, batch) = await BuildPendingFA(transactionType, faTransaction, result.ReasonCode);

            await DBfinance.SummFAFR_DERepository.CreateFaFrDeAsync(newTransaction);
            await DBfinance.ControlBatchRepository.UpdateBatchAsync(batch);

            return result;
        }

        private async Task<(SummFAFR_DE_Data, ControlBatchData)> BuildFA(string transactionType, SummFAFR_DE_Data data)
        {
            byte pend_SummFAFR_OrigFA_Ind = transactionType switch
            {
                TransactionType.FundAvailable => 1,
                TransactionType.FundReversal => 0,
                TransactionType.FundTransferred => 9,
                _ => 1
            };

            var newFaTransaction = new SummFAFR_DE_Data
            {
                Appl_EnfSrv_Cd = "FO01",
                Appl_CtrlCd = "PEND",
                Batch_Id = data.Batch_Id,
                SummFAFR_OrigFA_Ind = pend_SummFAFR_OrigFA_Ind,
                SummFAFRLiSt_Cd = 0,
                EnfSrv_Src_Cd = data.EnfSrv_Src_Cd,
                EnfSrv_Loc_Cd = data.EnfSrv_Loc_Cd,
                EnfSrv_SubLoc_Cd = data.EnfSrv_SubLoc_Cd,
                SummFAFR_FA_Pym_Id = data.SummFAFR_FA_Pym_Id,
                SummFAFR_MultiSumm_Ind = 0,
                SummFAFR_Post_Dte = DateTime.Now,
                SummFAFR_FA_Payable_Dte = data.SummFAFR_FA_Payable_Dte,
                SummFAFR_AvailDbtrAmt_Money = data.SummFAFR_AvailAmt_Money,
                SummFAFR_AvailAmt_Money = data.SummFAFR_AvailAmt_Money,
                Dbtr_Id = data.Dbtr_Id,
                SummFAFR_Comments = data.Dbtr_Id
            };

            var batch = await DBfinance.ControlBatchRepository.GetControlBatchAsync(data.Batch_Id);
            if (batch is null)
                throw new Exception("BatchID does not exist");

            batch.BatchLiSt_Cd = 1; // in progress

            return (newFaTransaction, batch);

        }

        private async Task<(SummFAFR_DE_Data, ControlBatchData)> BuildPendingFA(string transactionType, SummFAFR_DE_Data data,
                                                                                EventCode reasonCode)
        {
            byte pend_SummFAFR_OrigFA_Ind = transactionType switch
            {
                TransactionType.FundAvailable => 1,
                TransactionType.FundReversal => 0,
                TransactionType.FundTransferred => 9,
                _ => 1
            };

            var newPendingTransaction = new SummFAFR_DE_Data
            {
                Appl_EnfSrv_Cd = "FO01",
                Appl_CtrlCd = "PEND",
                Batch_Id = data.Batch_Id,
                SummFAFR_OrigFA_Ind = pend_SummFAFR_OrigFA_Ind,
                SummFAFRLiSt_Cd = 51,
                SummFAFR_Reas_Cd = (int)reasonCode,
                EnfSrv_Src_Cd = data.EnfSrv_Src_Cd,
                EnfSrv_Loc_Cd = data.EnfSrv_Loc_Cd,
                EnfSrv_SubLoc_Cd = data.EnfSrv_SubLoc_Cd,
                SummFAFR_FA_Pym_Id = data.SummFAFR_FA_Pym_Id,
                SummFAFR_MultiSumm_Ind = 0,
                SummFAFR_Post_Dte = DateTime.Now,
                SummFAFR_FA_Payable_Dte = data.SummFAFR_FA_Payable_Dte,
                SummFAFR_AvailDbtrAmt_Money = data.SummFAFR_AvailAmt_Money,
                SummFAFR_AvailAmt_Money = data.SummFAFR_AvailAmt_Money,
                Dbtr_Id = data.Dbtr_Id,
                SummFAFR_Comments = data.Dbtr_Id
            };

            var batch = await DBfinance.ControlBatchRepository.GetControlBatchAsync(data.Batch_Id);
            if (batch is null)
                throw new Exception("BatchID does not exist for InsertPendingForProcessingFAFR");

            batch.Batch_Pend_Ind = 1;
            if (batch.PendTtlAmt_Money is null)
                batch.PendTtlAmt_Money = data.SummFAFR_AvailAmt_Money;
            else
                batch.PendTtlAmt_Money += data.SummFAFR_AvailAmt_Money;
            batch.BatchLiSt_Cd = 1; // in progress

            return (newPendingTransaction, batch);

        }

        public async Task<SummFAFR_DE_Data> GetFaFrDe(int summFaFrDeId)
        {
            return await DBfinance.SummFAFR_DERepository.GetSummFaFrDeAsync(summFaFrDeId);
        }

        private async Task<TransactionResult> ValidateTransaction(string transactionType, SummFAFR_DE_Data newTransaction)
        {
            TransactionResult result;

            result = await ValidateDepartmentCode(newTransaction);
            if (result.ReturnCode == ReturnCode.Invalid)
                return result;

            (result, string sin) = await ValidateDebtorId(newTransaction);
            if (result.ReturnCode == ReturnCode.Invalid)
                return result;

            if (string.IsNullOrEmpty(newTransaction.Batch_Id))
                return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.UNDEFINED };

            result = await ValidateSin(newTransaction);
            if (result.ReturnCode == ReturnCode.Invalid)
                return result;

            result = ValidatePayableDate(newTransaction);
            if (result.ReturnCode == ReturnCode.Invalid)
                return result;

            result = ValidateAmounts("XPORT", newTransaction);
            if (result.ReturnCode == ReturnCode.Invalid)
                return result;

            return await VerifyDuplicateTransaction(transactionType, newTransaction);
        }

        private async Task<TransactionResult> ValidateDepartmentCode(SummFAFR_DE_Data data)
        {
            int count = await DB.EnfSrcTable.GetEnfSrcCount(data.EnfSrv_Src_Cd, data.EnfSrv_Loc_Cd, data.EnfSrv_SubLoc_Cd);
            if (count == 0)
                if (string.IsNullOrEmpty(data.EnfSrv_SubLoc_Cd))
                    return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.C57020_CTV_ERROR_ON_DEPARTMENT_LOCATOR_ID };
                else
                    return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.C57021_CTV_ERROR_ON_DEPARTMENT_SUBLOCATOR_ID };

            return new TransactionResult { ReturnCode = ReturnCode.Valid, ReasonCode = EventCode.UNDEFINED };
        }

        private async Task<(TransactionResult, string sin)> ValidateDebtorId(SummFAFR_DE_Data data)
        {
            string sin = data.SummFAFR_FA_Pym_Id[0..9];

            string debtorId = string.Empty;
            if ((data.EnfSrv_Src_Cd == "UI00") && (data.Dbtr_Id == "9999999"))
                debtorId = await DB.InterceptionTable.GetDebtorIdByConfirmedSin(sin, "I01");

            bool debtorExists = await DB.InterceptionTable.CheckDebtorIdExists(debtorId);
            if (!debtorExists)
                return (new TransactionResult
                {
                    ReturnCode = ReturnCode.Invalid,
                    ReasonCode = EventCode.C57001_INVALID_DEBTORID__COULD_NOT_LOCATE_ANY_SUMMONS
                }, sin);

            return (new TransactionResult
            {
                ReturnCode = ReturnCode.Valid,
                ReasonCode = EventCode.UNDEFINED
            }, sin);
        }

        private async Task<TransactionResult> ValidateSin(SummFAFR_DE_Data data)
        {
            var confirmedSinData = await DB.ApplicationTable.GetConfirmedSinByDebtorId(data.Dbtr_Id, isActiveOnly: false);
            if ((confirmedSinData is null) || !confirmedSinData.Any())
                confirmedSinData = await DB.ApplicationTable.GetConfirmedSinByDebtorId(data.Dbtr_Id, isActiveOnly: true);
            if ((confirmedSinData is null) || !confirmedSinData.Any())
                return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.UNDEFINED };

            var confirmedSin = confirmedSinData.First();

            if (await DBfinance.ControlBatchRepository.GetPaymentIdIsSinIndicator(data.Batch_Id))
                return new TransactionResult { ReturnCode = ReturnCode.Valid, ReasonCode = EventCode.UNDEFINED };

            if (data.SummFAFR_FA_Pym_Id[0..9] == confirmedSin.SVR_SIN)
                return new TransactionResult { ReturnCode = ReturnCode.Valid, ReasonCode = EventCode.UNDEFINED };

            return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.C57002_INVALID_SIN__DOES_NOT_MATCH_SIN_ON_SUMMONS };
        }

        private static TransactionResult ValidatePayableDate(SummFAFR_DE_Data data)
        {
            if (data.SummFAFR_FA_Payable_Dte > DateTime.Now)
                return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.C57003_WARNING__PAYABLE_DATE_IS_IN_FUTURE };

            return new TransactionResult { ReturnCode = ReturnCode.Valid, ReasonCode = EventCode.UNDEFINED };
        }

        private static TransactionResult ValidateAmounts(string processId, SummFAFR_DE_Data data)
        {
            if ((processId.ToUpper() != "ADJUST") && (data.SummFAFR_AvailAmt_Money < 0M))
                return new TransactionResult { ReturnCode = ReturnCode.Invalid, ReasonCode = EventCode.C57033_NEGATIVE_FA_OR_FT_ARE_NOT_ALLOWED };

            return new TransactionResult { ReturnCode = ReturnCode.Valid, ReasonCode = EventCode.UNDEFINED };
        }

        private async Task<TransactionResult> VerifyDuplicateTransaction(string transactionType, SummFAFR_DE_Data data)
        {
            if (transactionType == TransactionType.FundAvailable)
            {
                int dupCount = await DBfinance.SummFAFR_DERepository.CountDuplicateFundsAvailable(data.Dbtr_Id,
                                                                                                  data.EnfSrv_Src_Cd,
                                                                                                  data.EnfSrv_Loc_Cd,
                                                                                                  data.SummFAFR_FA_Pym_Id,
                                                                                                  data.SummFAFR_FA_Payable_Dte ?? DateTime.MinValue);

                if (dupCount > 0)
                    return new TransactionResult
                    {
                        ReturnCode = ReturnCode.Invalid,
                        ReasonCode = EventCode.C57013_PENDING__FUNDS_AVAILABLE_TRANSACTION_IS_IDENTICAL_TO_AN_EXISTING_FUNDS_AVAILABLE_TRANSACTION
                    };
            }
            else // FundReversal
            {
                int dupCount = await DBfinance.SummFAFR_DERepository.CountDuplicateFundsReversal(data.Dbtr_Id,
                                                                                                 data.EnfSrv_Src_Cd,
                                                                                                 data.EnfSrv_Loc_Cd,
                                                                                                 data.SummFAFR_FA_Pym_Id,
                                                                                                 data.SummFAFR_FA_Payable_Dte ?? DateTime.MinValue);
                if (dupCount > 0)
                    return new TransactionResult
                    {
                        ReturnCode = ReturnCode.Invalid,
                        ReasonCode = EventCode.C57025_DUPLICATE_FR
                    };
            }

            return new TransactionResult
            {
                ReturnCode = ReturnCode.Valid,
                ReasonCode = EventCode.UNDEFINED
            };
        }

    }
}
