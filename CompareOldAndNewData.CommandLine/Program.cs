﻿using CompareOldAndNewData.CommandLine;
using DBHelper;
using FileBroker.Data.DB;
using FOAEA3.Data.Base;
using FOAEA3.Resources.Helpers;
using Microsoft.Extensions.Configuration;

string aspnetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

IConfiguration configuration = new ConfigurationBuilder()
        .AddJsonFile($"appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{aspnetCoreEnvironment}.json", optional: true)
        .Build();

var foaea2DB = new DBTools(configuration.GetConnectionString("Foaea2DB").ReplaceVariablesWithEnvironmentValues());
var foaea3DB = new DBTools(configuration.GetConnectionString("Foaea3DB").ReplaceVariablesWithEnvironmentValues());
var fileBrokerDB = new DBTools(configuration.GetConnectionString("FileBroker").ReplaceVariablesWithEnvironmentValues());

var repositories2 = new DbRepositories(foaea2DB);
var repositories3 = new DbRepositories(foaea3DB);
var repositories2Finance = new DbRepositories_Finance(foaea2DB);
var repositories3Finance = new DbRepositories_Finance(foaea3DB);

var foaea2RunDate = (new DateTime(2022, 5, 25)).Date;
var foaea3RunDate = DateTime.Now.Date; // (new DateTime(2022, 6, 9)).Date;

var requestLogDB = new DBRequestLog(fileBrokerDB);
var requests = requestLogDB.GetAll();
foreach(var request in requests)
{
    var action = request.MaintenanceAction + request.MaintenanceLifeState;
    var enfSrv = request.Appl_EnfSrv_Cd;
    var ctrlCd = request.Appl_CtrlCd;
    CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, 
                   action, enfSrv, ctrlCd, foaea2RunDate, foaea3RunDate);
}

//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "ON01", "P02862", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "ON01", "O88291", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "ON01", "O36284", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C14", "ON01", "P02001", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C14", "ON01", "P23642", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C14", "ON01", "O62858", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C14", "ON01", "P75478", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "A00", "ON01", "P85061", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "A00", "ON01", "P85105", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "A00", "ON01", "P85100", foaea2RunDate, foaea3RunDate);

//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "QC01", "113352", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "QC01", "102609", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "QC01", "101265", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C17", "QC01", "119894", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C14", "QC01", "133007", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C14", "QC01", "127804", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "A00", "QC01", "140178", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "A00", "QC01", "140180", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "A00", "QC01", "140179", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C35", "QC01", "109452", foaea2RunDate, foaea3RunDate);
//CompareAll.Run(repositories2, repositories2Finance, repositories3, repositories3Finance, "C35", "QC01", "128706", foaea2RunDate, foaea3RunDate);

Console.WriteLine("\nFinished");

