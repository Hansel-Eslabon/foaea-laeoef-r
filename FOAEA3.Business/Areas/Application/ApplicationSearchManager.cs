﻿using FOAEA3.Model.Interfaces;
using FOAEA3.Model;
using FOAEA3.Model.Enums;
using System.Collections.Generic;
using FOAEA3.Business.Security;
using FOAEA3.Resources.Helpers;
using System.Threading.Tasks;

namespace FOAEA3.Business.Areas.Application
{
    internal class ApplicationSearchManager
    {
        private readonly IRepositories Repositories;
        private readonly AccessAuditManager AuditManager;

        public string LastError { get; set; }

        public ApplicationSearchManager(IRepositories repositories)
        {
            Repositories = repositories; 
            AuditManager = new AccessAuditManager(Repositories);
        }

        public async Task<(List<ApplicationSearchResultData>, int)> SearchAsync(QuickSearchData searchCriteria, 
                                                                           int page, int perPage, string orderBy)
        {
            int accessAuditId = await AuditManager.AddAuditHeaderAsync(AccessAuditPage.ApplicationSearch);
            await AddSearchCriteriaToAccessAuditAsync(accessAuditId, searchCriteria);

            List<ApplicationSearchResultData> searchResults;
            int totalCount;
            (searchResults, totalCount) = await Repositories.ApplicationSearchRepository.QuickSearchAsync(searchCriteria,
                                                                                         page, perPage, orderBy);

            LastError = Repositories.ApplicationSearchRepository.LastError;

            await AddSearchResultsToAccessAuditAsync(accessAuditId, searchResults);

            return (searchResults, totalCount);
        }

        private async Task AddSearchCriteriaToAccessAuditAsync(int accessAuditId, QuickSearchData searchCriteria)
        {
            var items = new Dictionary<AccessAuditElement, string>();

            if (!string.IsNullOrEmpty(searchCriteria.Category)) items.Add(AccessAuditElement.Category, searchCriteria.Category);
            if (!string.IsNullOrEmpty(searchCriteria.ControlCode)) items.Add(AccessAuditElement.CtrlCode, searchCriteria.ControlCode);
            if (!string.IsNullOrEmpty(searchCriteria.EnforcementService)) items.Add(AccessAuditElement.EnfSrv_Cd, searchCriteria.EnforcementService);
            if (!string.IsNullOrEmpty(searchCriteria.ReferenceNumber)) items.Add(AccessAuditElement.EnfRefSourceNr, searchCriteria.ReferenceNumber);
            if (!string.IsNullOrEmpty(searchCriteria.DebtorFirstName)) items.Add(AccessAuditElement.FirstName, searchCriteria.DebtorFirstName);
            if (!string.IsNullOrEmpty(searchCriteria.DebtorMiddleName)) items.Add(AccessAuditElement.MiddleName, searchCriteria.DebtorMiddleName);
            if (!string.IsNullOrEmpty(searchCriteria.DebtorSurname)) items.Add(AccessAuditElement.LastName, searchCriteria.DebtorSurname);
            if (!string.IsNullOrEmpty(searchCriteria.JusticeNumber)) items.Add(AccessAuditElement.JusticeNr, searchCriteria.JusticeNumber);
            if (!string.IsNullOrEmpty(searchCriteria.SIN)) items.Add(AccessAuditElement.SIN, searchCriteria.SIN);
            if (!string.IsNullOrEmpty(searchCriteria.Status)) items.Add(AccessAuditElement.Status, searchCriteria.Status);
            if (!string.IsNullOrEmpty(searchCriteria.Submitter)) items.Add(AccessAuditElement.Subm_SubmCd, searchCriteria.Submitter);
            if (searchCriteria.State != ApplicationState.UNDEFINED) items.Add(AccessAuditElement.State, ((int)searchCriteria.State).ToString());
            if (searchCriteria.DebtorDateOfBirth_Start.HasValue) items.Add(AccessAuditElement.DOBFrom, searchCriteria.DebtorDateOfBirth_Start.Value.ToString(DateTimeExtensions.YYYY_MM_DD));
            if (searchCriteria.DebtorDateOfBirth_End.HasValue) items.Add(AccessAuditElement.DOBTo, searchCriteria.DebtorDateOfBirth_End.Value.ToString(DateTimeExtensions.YYYY_MM_DD));
            if (searchCriteria.CreatedDate_Start.HasValue) items.Add(AccessAuditElement.ApplicationReceiptDateFrom, searchCriteria.CreatedDate_Start.Value.ToString(DateTimeExtensions.YYYY_MM_DD));
            if (searchCriteria.CreatedDate_End.HasValue) items.Add(AccessAuditElement.ApplicationReceiptDateTo, searchCriteria.CreatedDate_End.Value.ToString(DateTimeExtensions.YYYY_MM_DD));
            if (searchCriteria.ActualEndDate_Start.HasValue) items.Add(AccessAuditElement.ApplicationExpiryDateFrom, searchCriteria.ActualEndDate_Start.Value.ToString(DateTimeExtensions.YYYY_MM_DD));
            if (searchCriteria.ActualEndDate_End.HasValue) items.Add(AccessAuditElement.ApplicationExpiryDateTo, searchCriteria.ActualEndDate_End.Value.ToString(DateTimeExtensions.YYYY_MM_DD));
            if (searchCriteria.ViewAllJurisdiction) items.Add(AccessAuditElement.ViewAllJurisdictions, searchCriteria.ViewAllJurisdiction.ToString());
            if (searchCriteria.SearchOnlySinConfirmed) items.Add(AccessAuditElement.SINConfirmed, searchCriteria.SearchOnlySinConfirmed.ToString());

            await AuditManager.AddAuditElementsAsync(accessAuditId, items);
        }

        private async Task AddSearchResultsToAccessAuditAsync(int accessAuditId, List<ApplicationSearchResultData> searchResults)
        {
            foreach (var searchResult in searchResults)
            {
                string key = $"{searchResult.Appl_EnfSrv_Cd.Trim()}-{searchResult.Appl_CtrlCd.Trim()}";
                await AuditManager.AddAuditElementAsync(accessAuditId, AccessAuditElement.ApplicationID, key);
            }
        }

    }
}
