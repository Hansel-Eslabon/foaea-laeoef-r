﻿using FOAEA3.Model.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FOAEA3.Model.Interfaces.Repository
{
    public interface ITraceResponseRepository : IMessageList
    {
        string CurrentSubmitter { get; set; }
        string UserId { get; set; }

        Task<DataList<TraceResponseData>> GetTraceResponseForApplicationAsync(string applEnfSrvCd, string applCtrlCd, bool checkCycle = false);
        Task InsertBulkDataAsync(List<TraceResponseData> responseData);

        Task<DataList<TraceFinancialResponseData>> GetTraceResponseFinancialsForApplication(string applEnfSrvCd, string applCtrlCd);
        Task<DataList<TraceFinancialResponseDetailData>> GetTraceResponseFinancialDetails(int traceResponseFinancialId);
        Task<DataList<TraceFinancialResponseDetailValueData>> GetTraceResponseFinancialDetailValues(int traceResponseFinancialDetailId);
        Task<int> CreateTraceResponseFinancial(TraceFinancialResponseData data);
        Task<int> CreateTraceResponseFinancialDetail(TraceFinancialResponseDetailData data);
        Task<int> CreateTraceResponseFinancialDetailValue(TraceFinancialResponseDetailValueData data);
        Task UpdateTraceResponseFinancial(TraceFinancialResponseData data);

        Task DeleteCancelledApplicationTraceResponseDataAsync(string applEnfSrvCd, string applCtrlCd, string enfSrvCd);
        Task MarkResponsesAsViewedAsync(string enfService);
    }
}
