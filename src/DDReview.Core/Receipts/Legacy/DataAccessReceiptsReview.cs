#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using Microsoft.Data.SqlClient;
using DDReview.Core.Sales.Legacy;
using System.IO;

namespace DDReview.Core.Receipts.Legacy {

	public class DataAccessReceiptsReview : DataAccessReview {
		public Int32 StartDayNumber;
		public Int32 EndDayNumber;
		public Int32 WeekNumberFlag = 0;
		public Int32 StoreGroupFlag = 0;
		public String StoreGroupListCSV = "";
		public Int32 StoreFlag = 1;
		public String StoreListCSV = "";
		public Int32 ProfitCenterFlag = 0;
		public String ProfitCenterListCSV = "";
		public Int32 ReceiptClassFlag = 0;
		public String ReceiptClassListCSV = "";
		public Int32 ReceiptTypeFlag = 0;
		public String ReceiptTypeListCSV = "";
		public Int32 CreditCardBatchNumberFlag = 0;
		public String CreditCardBatchNumberListCSV = "";
		public Int32 CashierStaffDepartmentFlag = 0;
		public String CashierStaffDepartmentListCSV = "";
		public Int32 CashierFlag = 0;
		public String CashierListCSV = "";
		public Int32 ServerStaffDepartmentFlag = 0;
		public String ServerStaffDepartmentListCSV = "";
		public Int32 ServerFlag = 0;
		public String ServerListCSV = "";
		public Int32 CheckNumberFlag = 0;
		public String CheckNumberListCSV = "";
		public Int32 DayNameFlag=0;
		public String DayNameListCSV="";
		public Int32 WeekNameFlag=0;
		public String WeekNameListCSV="";
		public Int32 CashierSessionNumberFlag=0;
		public String CashierSessionNumberListCSV="";
		public Int32 DayHourNumberFlag=0;
		public String DayHourNumberListCSV="";
		public Boolean ProfitCenterFieldExists = true;
		private DateTime dtQueryStart;
		private DateTime dtQueryEnd;
		private TimeSpan tsQueryDuration;
		public int RowsReturned = 0;
		public string ConnectionString = "";
		public string SQL = "";
		public string SQLHeader = "";
		public string DefaultSort = "";
		public string BaseSort = "";
		public bool FillDataSet = true;
		public Dictionary<String,String> CalculatedFields = new Dictionary<String,String>();
		public void AddCalculatedField (String FieldName, String FieldExpression) {
			CalculatedFields.Add(FieldName, FieldExpression);
			}
		public DataAccessReceiptsReview(String strConnection) {
			ConnectionString = strConnection;
			}
		public double QueryDurationSeconds() {
			return (tsQueryDuration.TotalSeconds);
			}
		public String SQLSelectStatement;
		/// <summary>Builds the full report script (parameter header + select). The caller probes the database for the ProfitCenterID column first.</summary>
		public String BuildDataScript(Boolean profitCenterColumnExists, Boolean showDepositsFromDetail, Int32 receiptDetailsCashTipsEarnedId) {
			return RetrieveReceiptsDataSQL(profitCenterColumnExists, showDepositsFromDetail, receiptDetailsCashTipsEarnedId);
			}
		private class BasicSelect : Select {
			/* basic template */
			DataAccessReceiptsReview rv;
			public override String OutputSelect(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				sb.Append(@"
					select BatchID, StoreID, PostingDate
					from tblCashAndSalesBatches csb
					inner join tblStores sto
					on sto.StoreID = csb.StoreID
					where DayNumber between @StartDayNumber and @EndDFayNumber");
				return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
				}
			public BasicSelect(DataAccessReceiptsReview rv) {
				this.rv = rv;
				/* dimensions are defined here */
				}
			}
		private class ReceiptsSelect : Select {
			public Int32 ReceiptDetailsCashTipsEarnedID = 0;
			DataAccessReceiptsReview rv;
			public override String OutputSelect(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				sb.Append(@"
					declare @table_variable TABLE (
						RowID int identity (0,1) not null,
						StartingDate datetime,
						EndingDate datetime,
						Preliminary int,
						StoreGroupID int,
						StoreGroupName varchar(50),
						StoreGroupNumber int,
						StoreID int,
						StoreNumber int,
						StoreName varchar(50),
						StoreGCode varchar(30),
						ProfitCenterID int,
						ProfitCenter varchar(50),
						ReceiptClassID int,
						ReceiptClass varchar(50),
						ReceiptTypeID int,
						ReceiptType varchar(50),
						CreditCardBatchNumber int,
						CashierStaffDepartmentID int,
						CashierStaffDepartmentName varchar(50),
						CashierStaffID int,
						CashierName varchar(100),
						ServerStaffDepartmentID int,
						ServerStaffDepartmentName varchar(50),
						ServerStaffID int,
						ServerName varchar(100),
						CheckNumberID bigint,
						CheckNumber varchar(20),
						DayNameID int,
						DayName varchar(12),
						WeekNameID int,
						WeekName varchar(12),
						CashierSessionNumber int,
						DayHourNumber int,
						DayHourName varchar(20),
						Preliminary1 int,
						Preliminary2 int,
						Preliminary3 int,
						Preliminary4 int,
						Preliminary5 int,
						Preliminary6 int,
						Preliminary7 int,
						R1 money,
						R2 money,
						R3 money,
						R4 money,
						R5 money,
						R6 money,
						R7 money,
						C1 money,
						C2 money,
						C3 money,
						C4 money,
						C5 money,
						C6 money,
						C7 money,
						V1 money,
						V2 money,
						V3 money,
						V4 money,
						V5 money,
						V6 money,
						V7 money,
						D1 money,
						D2 money,
						D3 money,
						D4 money,
						D5 money,
						D6 money,
						D7 money,
						DepositCash1 money,
						DepositCash2 money,
						DepositCash3 money,
						DepositCash4 money,
						DepositCash5 money,
						DepositCash6 money,
						DepositCash7 money,
						DepositCreditCard1 money,
						DepositCreditCard2 money,
						DepositCreditCard3 money,
						DepositCreditCard4 money,
						DepositCreditCard5 money,
						DepositCreditCard6 money,
						DepositCreditCard7 money,
						DepositCreditCardVISA1 money,
						DepositCreditCardVISA2 money,
						DepositCreditCardVISA3 money,
						DepositCreditCardVISA4 money,
						DepositCreditCardVISA5 money,
						DepositCreditCardVISA6 money,
						DepositCreditCardVISA7 money,
						DepositCreditCardMC1 money,
						DepositCreditCardMC2 money,
						DepositCreditCardMC3 money,
						DepositCreditCardMC4 money,
						DepositCreditCardMC5 money,
						DepositCreditCardMC6 money,
						DepositCreditCardMC7 money,
						DepositCreditCardSwiped1 money,
						DepositCreditCardSwiped2 money,
						DepositCreditCardSwiped3 money,
						DepositCreditCardSwiped4 money,
						DepositCreditCardSwiped5 money,
						DepositCreditCardSwiped6 money,
						DepositCreditCardSwiped7 money,
						DepositCreditCardDiscover1 money,
						DepositCreditCardDiscover2 money,
						DepositCreditCardDiscover3 money,
						DepositCreditCardDiscover4 money,
						DepositCreditCardDiscover5 money,
						DepositCreditCardDiscover6 money,
						DepositCreditCardDiscover7 money,
						DepositCreditCardOther1 money,
						DepositCreditCardOther2 money,
						DepositCreditCardOther3 money,
						DepositCreditCardOther4 money,
						DepositCreditCardOther5 money,
						DepositCreditCardOther6 money,
						DepositCreditCardOther7 money,
						DepositCreditCardEMV1 money,
						DepositCreditCardEMV2 money,
						DepositCreditCardEMV3 money,
						DepositCreditCardEMV4 money,
						DepositCreditCardEMV5 money,
						DepositCreditCardEMV6 money,
						DepositCreditCardEMV7 money,
						DepositCreditCardOLO1 money,
						DepositCreditCardOLO2 money,
						DepositCreditCardOLO3 money,
						DepositCreditCardOLO4 money,
						DepositCreditCardOLO5 money,
						DepositCreditCardOLO6 money,
						DepositCreditCardOLO7 money,
						DepositCreditCardAmEx1 money,
						DepositCreditCardAmEx2 money,
						DepositCreditCardAmEx3 money,
						DepositCreditCardAmEx4 money,
						DepositCreditCardAmEx5 money,
						DepositCreditCardAmEx6 money,
						DepositCreditCardAmEx7 money,
						GiftCardRedeemedAmount1 money,
						GiftCardRedeemedAmount2 money,
						GiftCardRedeemedAmount3 money,
						GiftCardRedeemedAmount4 money,
						GiftCardRedeemedAmount5 money,
						GiftCardRedeemedAmount6 money,
						GiftCardRedeemedAmount7 money,
						ND1 money,
						ND2 money,
						ND3 money,
						ND4 money,
						ND5 money,
						ND6 money,
						ND7 money,
						Q1 money,
						Q2 money,
						Q3 money,
						Q4 money,
						Q5 money,
						Q6 money,
						Q7 money,
						CheckCount1 money,
						CheckCount2 money,
						CheckCount3 money,
						CheckCount4 money,
						CheckCount5 money,
						CheckCount6 money,
						CheckCount7 money,
						DeclaredCoverCount1 money,
						DeclaredCoverCount2 money,
						DeclaredCoverCount3 money,
						DeclaredCoverCount4 money,
						DeclaredCoverCount5 money,
						DeclaredCoverCount6 money,
						DeclaredCoverCount7 money,
						ReceiptAmount1 money,
						ReceiptAmount2 money,
						ReceiptAmount3 money,
						ReceiptAmount4 money,
						ReceiptAmount5 money,
						ReceiptAmount6 money,
						ReceiptAmount7 money,
						TipsPaidAmount1 money,
						TipsPaidAmount2 money,
						TipsPaidAmount3 money,
						TipsPaidAmount4 money,
						TipsPaidAmount5 money,
						TipsPaidAmount6 money,
						TipsPaidAmount7 money,
						TipsWithheldAmount1 money,
						TipsWithheldAmount2 money,
						TipsWithheldAmount3 money,
						TipsWithheldAmount4 money,
						TipsWithheldAmount5 money,
						TipsWithheldAmount6 money,
						TipsWithheldAmount7 money,
						GrossReceiptAmount1 money,
						GrossReceiptAmount2 money,
						GrossReceiptAmount3 money,
						GrossReceiptAmount4 money,
						GrossReceiptAmount5 money,
						GrossReceiptAmount6 money,
						GrossReceiptAmount7 money,
						TaxAmount1 money,
						TaxAmount2 money,
						TaxAmount3 money,
						TaxAmount4 money,
						TaxAmount5 money,
						TaxAmount6 money,
						TaxAmount7 money,
						ServerCommissionAmount1 money,
						ServerCommissionAmount2 money,
						ServerCommissionAmount3 money,
						ServerCommissionAmount4 money,
						ServerCommissionAmount5 money,
						ServerCommissionAmount6 money,
						ServerCommissionAmount7 money,
						GiftCardSoldAmount1 money,
						GiftCardSoldAmount2 money,
						GiftCardSoldAmount3 money,
						GiftCardSoldAmount4 money,
						GiftCardSoldAmount5 money,
						GiftCardSoldAmount6 money,
						GiftCardSoldAmount7 money,
						ARPaidInAmount1 money,
						ARPaidInAmount2 money,
						ARPaidInAmount3 money,
						ARPaidInAmount4 money,
						ARPaidInAmount5 money,
						ARPaidInAmount6 money,
						ARPaidInAmount7 money,
						PaidOutAmount1 money,
						PaidOutAmount2 money,
						PaidOutAmount3 money,
						PaidOutAmount4 money,
						PaidOutAmount5 money,
						PaidOutAmount6 money,
						PaidOutAmount7 money,
						ChargeAmount1 money,
						ChargeAmount2 money,
						ChargeAmount3 money,
						ChargeAmount4 money,
						ChargeAmount5 money,
						ChargeAmount6 money,
						ChargeAmount7 money,
						PaidOutAmount1N money,
						PaidOutAmount2N money,
						PaidOutAmount3N money,
						PaidOutAmount4N money,
						PaidOutAmount5N money,
						PaidOutAmount6N money,
						PaidOutAmount7N money,
						NetReceiptAmount1 money,
						NetReceiptAmount2 money,
						NetReceiptAmount3 money,
						NetReceiptAmount4 money,
						NetReceiptAmount5 money,
						NetReceiptAmount6 money,
						NetReceiptAmount7 money,
						DiscountAmount1 money,
						DiscountAmount2 money,
						DiscountAmount3 money,
						DiscountAmount4 money,
						DiscountAmount5 money,
						DiscountAmount6 money,
						DiscountAmount7 money,
						TotalSales1 money,
						TotalSales2 money,
						TotalSales3 money,
						TotalSales4 money,
						TotalSales5 money,
						TotalSales6 money,
						TotalSales7 money,
						TipsReceivedAmount1 money,
						TipsReceivedAmount2 money,
						TipsReceivedAmount3 money,
						TipsReceivedAmount4 money,
						TipsReceivedAmount5 money,
						TipsReceivedAmount6 money,
						TipsReceivedAmount7 money,
						TipsDeclaredAmount1 money,
						TipsDeclaredAmount2 money,
						TipsDeclaredAmount3 money,
						TipsDeclaredAmount4 money,
						TipsDeclaredAmount5 money,
						TipsDeclaredAmount6 money,
						TipsDeclaredAmount7 money,
						TipsAmount1 money,
						TipsAmount2 money,
						TipsAmount3 money,
						TipsAmount4 money,
						TipsAmount5 money,
						TipsAmount6 money,
						TipsAmount7 money,
						PayrollSalesAmount1 money,
						PayrollSalesAmount2 money,
						PayrollSalesAmount3 money,
						PayrollSalesAmount4 money,
						PayrollSalesAmount5 money,
						PayrollSalesAmount6 money,
						PayrollSalesAmount7 money,
						CreditCardSalesAmount1 money,
						CreditCardSalesAmount2 money,
						CreditCardSalesAmount3 money,
						CreditCardSalesAmount4 money,
						CreditCardSalesAmount5 money,
						CreditCardSalesAmount6 money,
						CreditCardSalesAmount7 money,
						CreditCardTipAmount1 money,
						CreditCardTipAmount2 money,
						CreditCardTipAmount3 money,
						CreditCardTipAmount4 money,
						CreditCardTipAmount5 money,
						CreditCardTipAmount6 money,
						CreditCardTipAmount7 money,
						AverageNetSalesPerPayment1 money,
						AverageNetSalesPerPayment2 money,
						AverageNetSalesPerPayment3 money,
						AverageNetSalesPerPayment4 money,
						AverageNetSalesPerPayment5 money,
						AverageNetSalesPerPayment6 money,
						AverageNetSalesPerPayment7 money,
						AverageTipsPerPayment1 money,
						AverageTipsPerPayment2 money,
						AverageTipsPerPayment3 money,
						AverageTipsPerPayment4 money,
						AverageTipsPerPayment5 money,
						AverageTipsPerPayment6 money,
						AverageTipsPerPayment7 money,
						TaxableSales1 money,
						TaxableSales2 money,
						TaxableSales3 money,
						TaxableSales4 money,
						TaxableSales5 money,
						TaxableSales6 money,
						TaxableSales7 money,
						NonTaxableSales1 money,
						NonTaxableSales2 money,
						NonTaxableSales3 money,
						NonTaxableSales4 money,
						NonTaxableSales5 money,
						NonTaxableSales6 money,
						NonTaxableSales7 money,
						BadPayments1 money,
						BadPayments2 money,
						BadPayments3 money,
						BadPayments4 money,
						BadPayments5 money,
						BadPayments6 money,
						BadPayments7 money,
						TotalReceipts money,
						TotalOverShort money,
						TotalOverShortMin money,
						NetReceipts money,
						TotalDeposit money,
						TotalDepositCash money,
						TotalDepositCreditCard money,
						TotalDepositCreditCardVISA money,
						TotalDepositCreditCardMC money,
						TotalDepositCreditCardSwiped money,
						TotalDepositCreditCardDiscover money,
						TotalDepositCreditCardOther money,
						TotalDepositCreditCardEMV money,
						TotalDepositCreditCardOLO money,
						TotalDepositCreditCardAmEx money,
						TotalGiftCardRedeemed money,
						TotalOtherNonDeposit money,
						TotalCount money,
						TotalCheckCount money,
						TotalDeclaredCoverCount money,
						TotalReceiptAmount money,
						TotalTipsPaidAmount money,
						TotalTipsWithheldAmount money,
						TotalPaidTipsVsNetPercentageAmount money,
						TotalGrossReceiptAmount money,
						TotalTaxAmount money,
						TotalServerCommissionAmount money,
						TotalGiftCardSoldAmount money,
						TotalARPaidInAmount money,
						TotalPaidOutAmount money,
						TotalPaidOutAmountN money,
						TotalGrossRevenueAmount money,
						TotalChargeAmount money,
						TotalNetReceiptAmount money,
						TotalDiscountAmount money,
						TotalSalesAmount money,
						TotalDiscountPercentage money,
						TotalTipsReceivedAmount money,
						TotalTipsDeclaredAmount money,
						TotalTipsAmount money,
						TotalPayrollSalesAmount money,
						TotalTipsVsNetPercentageAmount money,
						TotalFood money,
						TotalBeverage money,
						TotalOther money,
						TotalDiscount money,
						TotalTax money,
						AverageNetSalesPerPayment money,
						AverageTipsPerPayment money,
						TotalCreditCardSales money,
						TotalCreditCardTip money,
						TotalTaxableSales money,
						TotalNonTaxableSales money,
						TotalBadPayments money
						)
					declare @tblStoreGroups table (StoreGroupID int primary key, StoreGroupName varchar(50), StoreGroupNumber int)
					if len(@StoreGroupListCSV)>0
						insert into @tblStoreGroups
							select KeyValue, StoreGroupName, StoreGroupNumber
						from
							tblStoreGroups sg
						inner join
							fn_CreateKeyTableValueFromCSVString(@StoreGroupListCSV) csv
						on csv.KeyValue = sg.StoreGroupID
					else
						insert into @tblStoreGroups(StoreGroupID, StoreGroupName, StoreGroupNumber)
							select distinct StoreGroupID, StoreGroupName, StoreGroupNumber from tblStoreGroups
					declare @tblStores table(StoreID int primary key, StoreName varchar(50), StoreGCode varchar(50), StoreNumber int, StoreGroupID int)
					if len(@StoreListCSV)>0
						insert into @tblStores
						select KeyValue, isnull(tblStores.StoreReportName,tblStores.StoreName) as StoreName, tblStores.StoreGCode, tblStores.StoreNumber, tblStores.StoreGroupID
						from
							tblStores
						inner join
							fn_CreateKeyTableValueFromCSVString(@StoreListCSV) csv
						on csv.KeyValue=tblStores.StoreID
					else 
						insert into @tblStores(StoreID, StoreName, StoreGCode, StoreNumber, StoreGroupID)
						select distinct StoreID, isnull(StoreReportName,StoreName) as StoreName, StoreGCode, StoreNumber, StoreGroupID from tblStores where Active=1
					declare @tblReceiptClasses table (TableKey int primary key, ReceiptClass varchar(50))
					if len(@ReceiptClassListCSV)>0
						begin
						insert into @tblReceiptClasses (TableKey, ReceiptClass)
							select
								KeyValue,
									case KeyValue
										when 1 then 'Cash'
										when 2 then 'Credit Card'
										when 3 then 'Other'
										when 4 then 'GC Redeem'
										when 6 then 'Other'
										when 8 then 'Other'
										else 'Bad Payment'
									end
								as ReceiptClass
							from
								fn_CreateKeyTableValueFromCSVString(@ReceiptClassListCSV) rcl
						end
					else
						begin
						insert into @tblReceiptClasses (TableKey, ReceiptClass) values (1,'Cash')
						insert into @tblReceiptClasses (TableKey, ReceiptClass) values (2,'Credit Card')
						insert into @tblReceiptClasses (TableKey, ReceiptClass) values (3,'Other')
						insert into @tblReceiptClasses (TableKey, ReceiptClass) values (4,'GC Redeem')
						insert into @tblReceiptClasses (TableKey, ReceiptClass) values (1000,'Bad Payment')
						end
					declare @tblReceiptTypes table (TableKey bigint primary key, StoreID int, ReceiptTypeID int, ReceiptType varchar(50), ReceiptPostedAsDiscount bit, ReceiptClassID int, CreditCardReceipt bit)
					if len(@ReceiptTypeListCSV)>0
						insert into @tblReceiptTypes
						select
							rt.StoreID*4294967296+case when rt.ReceiptPostedAsDiscount=1 then -1 else 1 end*rt.ReceiptTypeID as TableKey,
							rt.StoreID,
							case when rt.ReceiptPostedAsDiscount=1 then -1 else 1 end*rt.ReceiptTypeID as ReceiptTypeID,
							rt.ReceiptType,
							rt.ReceiptPostedAsDiscount,
							case
								when isnull(rt.ReceiptClass,0)=0
									then isnull((select top 1 ReceiptClass from tblReceiptTypes prt where EODLine=rt.EODLine+1),3)
								when rt.ReceiptClass=4
									then 4
								when rt.ReceiptClass>3
									then 3
								else rt.ReceiptClass
							end as ReceiptClassID,
							case when rt.ReceiptClass in (2,8) then 1 else 0 end as CreditCardReceipt
						from
							tblReceiptTypes rt
						inner join
							@tblStores sto
						on sto.StoreID=rt.StoreID
						inner join (
							select * from tblReceiptTypes trt" + (rv.ReceiptTypeListCSV.Length==0?"":" where (" + ParsePairSetOutput(rv.ReceiptTypeListCSV,"trt.StoreID","trt.ReceiptTypeID")+")") + @"
							) srt
						on srt.StoreID=rt.StoreID and srt.ReceiptTypeID=rt.ReceiptTypeID				
					else
						insert into @tblReceiptTypes
						select
							rt.StoreID*4294967296+case when rt.ReceiptPostedAsDiscount=1 then -1 else 1 end*rt.ReceiptTypeID as TableKey,
							rt.StoreID,
							case when rt.ReceiptPostedAsDiscount=1 then -1 else 1 end*rt.ReceiptTypeID as ReceiptTypeID,
							rt.ReceiptType,
							rt.ReceiptPostedAsDiscount,
							case
								when isnull(rt.ReceiptClass,0)=0
									then isnull((select top 1 ReceiptClass from tblReceiptTypes prt where EODLine=rt.EODLine+1),3)
								when rt.ReceiptClass=4
									then 4
								when rt.ReceiptClass>3
									then 3
								else rt.ReceiptClass
							end as ReceiptClassID,
							case when rt.ReceiptClass in (2,8) then 1 else 0 end as CreditCardReceipt
						from
							tblReceiptTypes rt
						inner join
							@tblStores sto
						on sto.StoreID=rt.StoreID
					declare @tblProfitCenters table (TableKey bigint primary key, StoreID int, ProfitCenterID int, ProfitCenter varchar(50), TipsWithheld bit)
					if len(@ProfitCenterListCSV)>0
						insert into @tblProfitCenters (TableKey, StoreID, ProfitCenterID, ProfitCenter, TipsWithheld)
							select
								rt.StoreID*4294967296*rt.ProfitCenterID as TableKey,
								rt.StoreID,
								rt.ProfitCenterID,
								rt.ProfitCenter,
								rt.TipsWithheld
							from
								tblProfitCenters rt with (nolock)
							inner join
								@tblStores sto
							on sto.StoreID=rt.StoreID
							inner join (
								select * from tblProfitCenters pft with (nolock)" + (rv.ProfitCenterListCSV.Length==0?"":" where (" + ParsePairSetOutput(rv.ProfitCenterListCSV,"pft.StoreID","pft.ProfitCenterID")+")") + @"
								) oft
							on oft.StoreID=rt.StoreID and oft.ProfitCenterID=rt.ProfitCenterID				
					else
						insert into @tblProfitCenters (TableKey, StoreID, ProfitCenterID, ProfitCenter, TipsWithheld)
							select
								rt.StoreID*4294967296+rt.ProfitCenterID as TableKey,
								rt.StoreID,
								rt.ProfitCenterID as ProfitCenterID,
								rt.ProfitCenter,
								rt.TipsWithheld
							from
								tblProfitCenters rt with (nolock)
							inner join
								@tblStores sto
							on sto.StoreID=rt.StoreID
					declare @tblStaffCashierStaffDepartmentNames table (TableKey bigint primary key, StoreID int, StaffDepartmentID int, StaffDepartmentName varchar(100), StaffDepartmentSort varchar(100))");
					if (rv.CashierStaffDepartmentListCSV.Length>0)
						sb.Append(@"
						insert into @tblStaffCashierStaffDepartmentNames (TableKey, StoreID, StaffDepartmentID, StaffDepartmentName)
						select
							sd.StoreID*4294967296+sd.StaffDepartmentID as TableKey, sd.StoreID, sd.StaffDepartmentID, sd.StaffDepartmentName
						from
							tblStaffDepartments sd with (nolock)
						inner join
							@tblStores sto
						on sto.StoreID=sd.StoreID
						inner join (
							select StoreID, StaffDepartmentID from tblStaffDepartments sd with (nolock) where (" + ParsePairSetOutput(rv.CashierStaffDepartmentListCSV,"sd.StoreID","sd.StaffDepartmentID") + @")
							) srt
						on srt.StoreID=sd.StoreID
							and srt.StaffDepartmentID = sd.StaffDepartmentID");
					else
						sb.Append(@"
						insert into @tblStaffCashierStaffDepartmentNames (TableKey, StoreID, StaffDepartmentID, StaffDepartmentName)
						select
							sd.StoreID*4294967296+sd.StaffDepartmentID as TableKey, sd.StoreID, sd.StaffDepartmentID, sd.StaffDepartmentName
						from
							tblStaffDepartments sd with (nolock)
						inner join
							@tblStores sto
						on sto.StoreID=sd.StoreID");
					sb.Append(@"
					insert into @tblStaffCashierStaffDepartmentNames (TableKey, StoreID, StaffDepartmentID, StaffDepartmentName)
						select sto.StoreID*4294967296+0, sto.StoreID, 0 as StaffDepartmentID, ' [Not Applicable]' as StaffDepartmentName from @tblStores sto
					declare @RedirectedReceiptsTable table (
						BatchID int,
						RedirectedReceiptTypeID int,
						RedirectedReceiptClassID int,
						ExcludedReceiptTypeID int,
						Expected money,
						Variance money
						)
					insert into @RedirectedReceiptsTable (
						BatchID,
						RedirectedReceiptTypeID,
						RedirectedReceiptClassID,
						ExcludedReceiptTypeID,
						Expected,
						Variance
						)
					select
						csr.BatchID,
						rq.RedirectedReceiptTypeID,
						rq.RedirectedReceiptClassID,
						rq.ExcludedReceiptTypeID,
						sum(csr.Expected) as Expected,
						sum(csr.Variance) as Variance
					from (
						select
							StoreID,
							ExcludedReceiptTypeID,
							min(RedirectedReceiptTypeID) as RedirectedReceiptTypeID,
							(select top 1 ReceiptClass from tblReceiptTypes with (nolock) where StoreID=qq.StoreID and ReceiptTypeID=min(qq.RedirectedReceiptTypeID)) as RedirectedReceiptClassID
						from (
							select
								iq.StoreID,
								(select top 1 rt.ReceiptTypeID from tblReceiptTypes rt with (nolock) where rt.StoreID=iq.StoreID and rt.EODLine=iq.MainEODLine) as ExcludedReceiptTypeID,
								iq.RedirectedReceiptTypeID
							from (
								select
									rt.StoreID,
									rt.ReceiptTypeID as RedirectedReceiptTypeID, (
										select max(EODLine) from tblReceiptTypes with (nolock) where StoreID=rt.StoreID and EODLine>0 and EODLine<rt.EODLine and ReceiptTypeID<100
										) as MainEODLine
								from
									tblReceiptTypes rt with (nolock)
								where ReceiptTypeID>100 and ReceiptTypeID<999
								) iq
							) qq
						group by
							StoreID,
							ExcludedReceiptTypeID
						) rq
					inner join
						tblCashAndSalesBatches csb with (nolock)
					on csb.StoreID=rq.StoreID
					inner join
						tblCashAndSalesReceipts csr with (nolock)
					on csr.BatchID=csb.BatchID
						and rq.ExcludedReceiptTypeID=csr.ReceiptTypeID
					where csb.DayNumber between @StartDayNumber and @EndDayNumber
					group by
						csr.BatchID,
						rq.RedirectedReceiptTypeID,
						rq.RedirectedReceiptClassID,
						rq.ExcludedReceiptTypeID
					declare @store_table TABLE (
						StartingDate datetime,
						EndingDate datetime,
						Preliminary int,
						StoreGroupID int,
						StoreGroupName varchar(50),
						StoreGroupNumber int,
						StoreID int,
						StoreNumber int,
						StoreName varchar(50),
						StoreGCode varchar(50),
						ProfitCenterID int,
						ProfitCenter varchar(50),
						ReceiptClassID int,
						ReceiptClass varchar(50),
						ReceiptTypeID int,
						ReceiptType varchar(50),
						CreditCardBatchNumber int,
						CashierStaffDepartmentID int,
						CashierStaffDepartmentName varchar(100),
						CashierStaffID int,
						CashierName varchar(100),
						ServerStaffDepartmentID int,
						ServerStaffDepartmentName varchar(100),
						ServerStaffID int,
						ServerName varchar(100),
						CheckNumberID bigint,
						CheckNumber varchar(20),
						DayNameID bigint,
						DayName varchar(20),
						WeekNameID bigint,
						WeekName varchar(20),
						CashierSessionNumber int,
						DayHourNumber int,
						Preliminary1 int,
						Preliminary2 int,
						Preliminary3 int,
						Preliminary4 int,
						Preliminary5 int,
						Preliminary6 int,
						Preliminary7 int,
						R1 money,
						R2 money,
						R3 money,
						R4 money,
						R5 money,
						R6 money,
						R7 money,
						C1 money,
						C2 money,
						C3 money,
						C4 money,
						C5 money,
						C6 money,
						C7 money,
						V1 money,
						V2 money,
						V3 money,
						V4 money,
						V5 money,
						V6 money,
						V7 money,
						D1 money,
						D2 money,
						D3 money,
						D4 money,
						D5 money,
						D6 money,
						D7 money,
						DepositCash1 money,
						DepositCash2 money,
						DepositCash3 money,
						DepositCash4 money,
						DepositCash5 money,
						DepositCash6 money,
						DepositCash7 money,
						DepositCreditCard1 money,
						DepositCreditCard2 money,
						DepositCreditCard3 money,
						DepositCreditCard4 money,
						DepositCreditCard5 money,
						DepositCreditCard6 money,
						DepositCreditCard7 money,
						DepositCreditCardVISA1 money,
						DepositCreditCardVISA2 money,
						DepositCreditCardVISA3 money,
						DepositCreditCardVISA4 money,
						DepositCreditCardVISA5 money,
						DepositCreditCardVISA6 money,
						DepositCreditCardVISA7 money,
						DepositCreditCardMC1 money,
						DepositCreditCardMC2 money,
						DepositCreditCardMC3 money,
						DepositCreditCardMC4 money,
						DepositCreditCardMC5 money,
						DepositCreditCardMC6 money,
						DepositCreditCardMC7 money,
						DepositCreditCardSwiped1 money,
						DepositCreditCardSwiped2 money,
						DepositCreditCardSwiped3 money,
						DepositCreditCardSwiped4 money,
						DepositCreditCardSwiped5 money,
						DepositCreditCardSwiped6 money,
						DepositCreditCardSwiped7 money,
						DepositCreditCardDiscover1 money,
						DepositCreditCardDiscover2 money,
						DepositCreditCardDiscover3 money,
						DepositCreditCardDiscover4 money,
						DepositCreditCardDiscover5 money,
						DepositCreditCardDiscover6 money,
						DepositCreditCardDiscover7 money,
						DepositCreditCardOther1 money,
						DepositCreditCardOther2 money,
						DepositCreditCardOther3 money,
						DepositCreditCardOther4 money,
						DepositCreditCardOther5 money,
						DepositCreditCardOther6 money,
						DepositCreditCardOther7 money,
						DepositCreditCardEMV1 money,
						DepositCreditCardEMV2 money,
						DepositCreditCardEMV3 money,
						DepositCreditCardEMV4 money,
						DepositCreditCardEMV5 money,
						DepositCreditCardEMV6 money,
						DepositCreditCardEMV7 money,
						DepositCreditCardOLO1 money,
						DepositCreditCardOLO2 money,
						DepositCreditCardOLO3 money,
						DepositCreditCardOLO4 money,
						DepositCreditCardOLO5 money,
						DepositCreditCardOLO6 money,
						DepositCreditCardOLO7 money,
						DepositCreditCardAmEx1 money,
						DepositCreditCardAmEx2 money,
						DepositCreditCardAmEx3 money,
						DepositCreditCardAmEx4 money,
						DepositCreditCardAmEx5 money,
						DepositCreditCardAmEx6 money,
						DepositCreditCardAmEx7 money,
						GiftCardRedeemedAmount1 money,
						GiftCardRedeemedAmount2 money,
						GiftCardRedeemedAmount3 money,
						GiftCardRedeemedAmount4 money,
						GiftCardRedeemedAmount5 money,
						GiftCardRedeemedAmount6 money,
						GiftCardRedeemedAmount7 money,
						ND1 money,
						ND2 money,
						ND3 money,
						ND4 money,
						ND5 money,
						ND6 money,
						ND7 money,
						Q1 money,
						Q2 money,
						Q3 money,
						Q4 money,
						Q5 money,
						Q6 money,
						Q7 money,
						CheckCount1 money,
						CheckCount2 money,
						CheckCount3 money,
						CheckCount4 money,
						CheckCount5 money,
						CheckCount6 money,
						CheckCount7 money,
						DeclaredCoverCount1 money,
						DeclaredCoverCount2 money,
						DeclaredCoverCount3 money,
						DeclaredCoverCount4 money,
						DeclaredCoverCount5 money,
						DeclaredCoverCount6 money,
						DeclaredCoverCount7 money,
						ReceiptAmount1 money,
						ReceiptAmount2 money,
						ReceiptAmount3 money,
						ReceiptAmount4 money,
						ReceiptAmount5 money,
						ReceiptAmount6 money,
						ReceiptAmount7 money,
						TipsPaidAmount1 money,
						TipsPaidAmount2 money,
						TipsPaidAmount3 money,
						TipsPaidAmount4 money,
						TipsPaidAmount5 money,
						TipsPaidAmount6 money,
						TipsPaidAmount7 money,
						TipsWithheldAmount1 money,
						TipsWithheldAmount2 money,
						TipsWithheldAmount3 money,
						TipsWithheldAmount4 money,
						TipsWithheldAmount5 money,
						TipsWithheldAmount6 money,
						TipsWithheldAmount7 money,
						GrossReceiptAmount1 money,
						GrossReceiptAmount2 money,
						GrossReceiptAmount3 money,
						GrossReceiptAmount4 money,
						GrossReceiptAmount5 money,
						GrossReceiptAmount6 money,
						GrossReceiptAmount7 money,
						TaxAmount1 money,
						TaxAmount2 money,
						TaxAmount3 money,
						TaxAmount4 money,
						TaxAmount5 money,
						TaxAmount6 money,
						TaxAmount7 money,
						ServerCommissionAmount1 money,
						ServerCommissionAmount2 money,
						ServerCommissionAmount3 money,
						ServerCommissionAmount4 money,
						ServerCommissionAmount5 money,
						ServerCommissionAmount6 money,
						ServerCommissionAmount7 money,
						GiftCardSoldAmount1 money,
						GiftCardSoldAmount2 money,
						GiftCardSoldAmount3 money,
						GiftCardSoldAmount4 money,
						GiftCardSoldAmount5 money,
						GiftCardSoldAmount6 money,
						GiftCardSoldAmount7 money,
						ARPaidInAmount1 money,
						ARPaidInAmount2 money,
						ARPaidInAmount3 money,
						ARPaidInAmount4 money,
						ARPaidInAmount5 money,
						ARPaidInAmount6 money,
						ARPaidInAmount7 money,
						PaidOutAmount1 money,
						PaidOutAmount2 money,
						PaidOutAmount3 money,
						PaidOutAmount4 money,
						PaidOutAmount5 money,
						PaidOutAmount6 money,
						PaidOutAmount7 money,
						ChargeAmount1 money,
						ChargeAmount2 money,
						ChargeAmount3 money,
						ChargeAmount4 money,
						ChargeAmount5 money,
						ChargeAmount6 money,
						ChargeAmount7 money,
						NetReceiptAmount1 money,
						NetReceiptAmount2 money,
						NetReceiptAmount3 money,
						NetReceiptAmount4 money,
						NetReceiptAmount5 money,
						NetReceiptAmount6 money,
						NetReceiptAmount7 money,
						DiscountAmount1 money,
						DiscountAmount2 money,
						DiscountAmount3 money,
						DiscountAmount4 money,
						DiscountAmount5 money,
						DiscountAmount6 money,
						DiscountAmount7 money,
						TipsReceivedAmount1 money,
						TipsReceivedAmount2 money,
						TipsReceivedAmount3 money,
						TipsReceivedAmount4 money,
						TipsReceivedAmount5 money,
						TipsReceivedAmount6 money,
						TipsReceivedAmount7 money,
						TipsDeclaredAmount1 money,
						TipsDeclaredAmount2 money,
						TipsDeclaredAmount3 money,
						TipsDeclaredAmount4 money,
						TipsDeclaredAmount5 money,
						TipsDeclaredAmount6 money,
						TipsDeclaredAmount7 money,
						TipsAmount1 money,
						TipsAmount2 money,
						TipsAmount3 money,
						TipsAmount4 money,
						TipsAmount5 money,
						TipsAmount6 money,
						TipsAmount7 money,
						PayrollSalesAmount1 money,
						PayrollSalesAmount2 money,
						PayrollSalesAmount3 money,
						PayrollSalesAmount4 money,
						PayrollSalesAmount5 money,
						PayrollSalesAmount6 money,
						PayrollSalesAmount7 money,
						CreditCardSalesAmount1 money,
						CreditCardSalesAmount2 money,
						CreditCardSalesAmount3 money,
						CreditCardSalesAmount4 money,
						CreditCardSalesAmount5 money,
						CreditCardSalesAmount6 money,
						CreditCardSalesAmount7 money,
						CreditCardTipAmount1 money,
						CreditCardTipAmount2 money,
						CreditCardTipAmount3 money,
						CreditCardTipAmount4 money,
						CreditCardTipAmount5 money,
						CreditCardTipAmount6 money,
						CreditCardTipAmount7 money,
						TaxableSales1 money,
						TaxableSales2 money,
						TaxableSales3 money,
						TaxableSales4 money,
						TaxableSales5 money,
						TaxableSales6 money,
						TaxableSales7 money,
						NonTaxableSales1 money,
						NonTaxableSales2 money,
						NonTaxableSales3 money,
						NonTaxableSales4 money,
						NonTaxableSales5 money,
						NonTaxableSales6 money,
						NonTaxableSales7 money,
						BadPayments1 money,
						BadPayments2 money,
						BadPayments3 money,
						BadPayments4 money,
						BadPayments5 money,
						BadPayments6 money,
						BadPayments7 money,
						TotalReceipts money,
						TotalOverShort money,
						NetReceipts money,
						TotalDeposit money,
						TotalDepositCash money,
						TotalDepositCreditCard money,
						TotalDepositCreditCardVISA money,
						TotalDepositCreditCardMC money,
						TotalDepositCreditCardSwiped money,
						TotalDepositCreditCardDiscover money,
						TotalDepositCreditCardOther money,
						TotalDepositCreditCardEMV money,
						TotalDepositCreditCardOLO money,
						TotalDepositCreditCardAmEx money,
						TotalGiftCardRedeemed money,
						TotalOtherNonDeposit money,
						TotalCount money,
						TotalCheckCount money,
						TotalDeclaredCoverCount money,
						TotalReceiptAmount money,
						TotalTipsPaidAmount money,
						TotalTipsWithheldAmount money,
						TotalGrossReceiptAmount money,
						TotalTaxAmount money,
						TotalServerCommissionAmount money,
						TotalGiftCardSoldAmount money,
						TotalARPaidInAmount money,
						TotalPaidOutAmount money,
						TotalChargeAmount money,
						TotalNetReceiptAmount money,
						TotalDiscountAmount money,
						TotalTipsReceivedAmount money,
						TotalTipsDeclaredAmount money,
						TotalTipsAmount money,
						TotalPayrollSalesAmount money,
						TotalFood money,
						TotalBeverage money,
						TotalOther money,
						TotalDiscount money,
						TotalTax money,
						TotalCreditCardSales money,
						TotalCreditCardTip money,
						TotalTaxableSales money,
						TotalNonTaxableSales money,
						TotalBadPayments money
						)
					declare @StoresDefined bit
					set @StoresDefined = 1
					if @StoreFlag=0 and @CheckNumberFlag=0 and (select count(*) from dbo.fn_CreateKeyTableValueFromCSVString(@StoreListCSV))<>1
						set @StoresDefined = 0
					insert into @store_table");
						ReceiptsOuterSelect ReceiptsOuterSelect = new ReceiptsOuterSelect(rv, 6);
						ReceiptsOuterSelect.ReceiptDetailsCashTipsEarnedID = this.ReceiptDetailsCashTipsEarnedID;
						ReceiptsOuterSelect.OutputDetailFields = this.OutputDetailFields;
						sb.Append(ReceiptsOuterSelect.OutputSelect());
						sb.Append(@"
					insert into @table_variable (
						StartingDate, EndingDate, Preliminary, StoreGroupID, StoreGroupName, StoreGroupNumber, StoreID, StoreNumber,
						StoreName, StoreGCode, ProfitCenterID, ProfitCenter, ReceiptClass, ReceiptClassID, ReceiptType, ReceiptTypeID, CreditCardBatchNumber, 
						CashierStaffDepartmentID, CashierStaffDepartmentName, CashierStaffID, CashierName, ServerStaffDepartmentID,
						ServerStaffDepartmentName, ServerStaffID, ServerName, CheckNumberID, CheckNumber,
						DayNameID, DayName, WeekNameID, WeekName, CashierSessionNumber, DayHourNumber,
						Preliminary1, Preliminary2, Preliminary3, Preliminary4, Preliminary5, Preliminary6, Preliminary7, 
						R1, R2, R3, R4, R5, R6, R7, C1, C2, C3, C4, C5, C6, C7, V1, V2, V3, V4, V5, V6, V7, D1, D2, D3, D4, D5, D6, D7, 
						DepositCash1, DepositCash2, DepositCash3, DepositCash4, DepositCash5, DepositCash6, DepositCash7, 
						DepositCreditCard1, DepositCreditCard2, DepositCreditCard3, DepositCreditCard4, DepositCreditCard5, DepositCreditCard6, DepositCreditCard7, 
						DepositCreditCardVISA1, DepositCreditCardVISA2, DepositCreditCardVISA3, DepositCreditCardVISA4, DepositCreditCardVISA5, DepositCreditCardVISA6, DepositCreditCardVISA7, 
						DepositCreditCardMC1, DepositCreditCardMC2, DepositCreditCardMC3, DepositCreditCardMC4, DepositCreditCardMC5, DepositCreditCardMC6, DepositCreditCardMC7, 
						DepositCreditCardSwiped1, DepositCreditCardSwiped2, DepositCreditCardSwiped3, DepositCreditCardSwiped4, DepositCreditCardSwiped5, DepositCreditCardSwiped6, DepositCreditCardSwiped7, 
						DepositCreditCardDiscover1, DepositCreditCardDiscover2, DepositCreditCardDiscover3, DepositCreditCardDiscover4, DepositCreditCardDiscover5, DepositCreditCardDiscover6, DepositCreditCardDiscover7, 
						DepositCreditCardOther1, DepositCreditCardOther2, DepositCreditCardOther3, DepositCreditCardOther4, DepositCreditCardOther5, DepositCreditCardOther6, DepositCreditCardOther7, 
						DepositCreditCardEMV1, DepositCreditCardEMV2, DepositCreditCardEMV3, DepositCreditCardEMV4, DepositCreditCardEMV5, DepositCreditCardEMV6, DepositCreditCardEMV7, 
						DepositCreditCardOLO1, DepositCreditCardOLO2, DepositCreditCardOLO3, DepositCreditCardOLO4, DepositCreditCardOLO5, DepositCreditCardOLO6, DepositCreditCardOLO7, 
						DepositCreditCardAmEx1, DepositCreditCardAmEx2, DepositCreditCardAmEx3, DepositCreditCardAmEx4, DepositCreditCardAmEx5, DepositCreditCardAmEx6, DepositCreditCardAmEx7, 
						GiftCardRedeemedAmount1, GiftCardRedeemedAmount2, GiftCardRedeemedAmount3, GiftCardRedeemedAmount4, GiftCardRedeemedAmount5, GiftCardRedeemedAmount6, GiftCardRedeemedAmount7, 
						ND1, ND2, ND3, ND4, ND5, ND6, ND7,
						Q1, Q2, Q3, Q4, Q5, Q6, Q7,
						CheckCount1, CheckCount2, CheckCount3, CheckCount4, CheckCount5, CheckCount6, CheckCount7,
						DeclaredCoverCount1, DeclaredCoverCount2, DeclaredCoverCount3, DeclaredCoverCount4, DeclaredCoverCount5, DeclaredCoverCount6, DeclaredCoverCount7,
						ReceiptAmount1, ReceiptAmount2, ReceiptAmount3, ReceiptAmount4, ReceiptAmount5, ReceiptAmount6, ReceiptAmount7, 
						TipsPaidAmount1, TipsPaidAmount2, TipsPaidAmount3, TipsPaidAmount4, TipsPaidAmount5, TipsPaidAmount6, TipsPaidAmount7, 
						TipsWithheldAmount1, TipsWithheldAmount2, TipsWithheldAmount3, TipsWithheldAmount4, TipsWithheldAmount5, TipsWithheldAmount6, TipsWithheldAmount7, 
						GrossReceiptAmount1, GrossReceiptAmount2, GrossReceiptAmount3, GrossReceiptAmount4, GrossReceiptAmount5, GrossReceiptAmount6, GrossReceiptAmount7, 
						TaxAmount1, TaxAmount2, TaxAmount3, TaxAmount4, TaxAmount5, TaxAmount6, TaxAmount7, 
						ServerCommissionAmount1, ServerCommissionAmount2, ServerCommissionAmount3, ServerCommissionAmount4, ServerCommissionAmount5, ServerCommissionAmount6, ServerCommissionAmount7, 
						GiftCardSoldAmount1, GiftCardSoldAmount2, GiftCardSoldAmount3, GiftCardSoldAmount4, GiftCardSoldAmount5, GiftCardSoldAmount6, GiftCardSoldAmount7, 
						ARPaidInAmount1, ARPaidInAmount2, ARPaidInAmount3, ARPaidInAmount4, ARPaidInAmount5, ARPaidInAmount6, ARPaidInAmount7, 
						PaidOutAmount1, PaidOutAmount2, PaidOutAmount3, PaidOutAmount4, PaidOutAmount5, PaidOutAmount6, PaidOutAmount7, 
						PaidOutAmount1N, PaidOutAmount2N, PaidOutAmount3N, PaidOutAmount4N, PaidOutAmount5N, PaidOutAmount6N, PaidOutAmount7N, 
						ChargeAmount1, ChargeAmount2, ChargeAmount3, ChargeAmount4, ChargeAmount5, ChargeAmount6, ChargeAmount7, 
						NetReceiptAmount1, NetReceiptAmount2, NetReceiptAmount3, NetReceiptAmount4, NetReceiptAmount5, NetReceiptAmount6, NetReceiptAmount7, 
						DiscountAmount1, DiscountAmount2, DiscountAmount3, DiscountAmount4, DiscountAmount5, DiscountAmount6, DiscountAmount7, 
						TipsReceivedAmount1, TipsReceivedAmount2, TipsReceivedAmount3, TipsReceivedAmount4, TipsReceivedAmount5, TipsReceivedAmount6, TipsReceivedAmount7, 
						TipsDeclaredAmount1, TipsDeclaredAmount2, TipsDeclaredAmount3, TipsDeclaredAmount4, TipsDeclaredAmount5, TipsDeclaredAmount6, TipsDeclaredAmount7, 
						PayrollSalesAmount1, PayrollSalesAmount2, PayrollSalesAmount3, PayrollSalesAmount4, PayrollSalesAmount5, PayrollSalesAmount6, PayrollSalesAmount7, 
						CreditCardSalesAmount1, CreditCardSalesAmount2, CreditCardSalesAmount3, CreditCardSalesAmount4, CreditCardSalesAmount5, CreditCardSalesAmount6, CreditCardSalesAmount7, 
						CreditCardTipAmount1, CreditCardTipAmount2, CreditCardTipAmount3, CreditCardTipAmount4, CreditCardTipAmount5, CreditCardTipAmount6, CreditCardTipAmount7, 
						TaxableSales1, TaxableSales2, TaxableSales3, TaxableSales4, TaxableSales5, TaxableSales6, TaxableSales7,
						NonTaxableSales1, NonTaxableSales2, NonTaxableSales3, NonTaxableSales4, NonTaxableSales5, NonTaxableSales6, NonTaxableSales7,
						BadPayments1, BadPayments2, BadPayments3, BadPayments4, BadPayments5, BadPayments6, BadPayments7,
						TotalReceipts, TotalOverShort, TotalOverShortMin, NetReceipts, TotalDeposit, TotalDepositCash, TotalDepositCreditCard, TotalDepositCreditCardVISA,
						TotalDepositCreditCardMC, TotalDepositCreditCardSwiped, TotalDepositCreditCardDiscover, TotalDepositCreditCardOther, TotalDepositCreditCardEMV, TotalDepositCreditCardOLO, TotalDepositCreditCardAmEx, TotalGiftCardRedeemed,
						TotalOtherNonDeposit, TotalCount, TotalCheckCount, TotalDeclaredCoverCount, TotalReceiptAmount, TotalTipsPaidAmount, TotalTipsWithheldAmount, TotalGrossReceiptAmount, TotalTaxAmount, TotalServerCommissionAmount,
						TotalGiftCardSoldAmount, TotalARPaidInAmount, TotalPaidOutAmount, TotalPaidOutAmountN, TotalGrossRevenueAmount, TotalChargeAmount, TotalNetReceiptAmount, TotalDiscountAmount, TotalTipsReceivedAmount, TotalTipsDeclaredAmount,
						TotalPayrollSalesAmount, TotalFood, TotalBeverage, TotalOther, TotalDiscount, TotalTax, TotalCreditCardSales, TotalCreditCardTip,
						TotalTaxableSales, TotalNonTaxableSales, TotalBadPayments
						)
					select min(StartingDate) as StartingDate,
						min(EndingDate) as EndingDate,
						case when sum(Preliminary)<>0 then 1 else 0 end as Preliminary,
						case when @StoreGroupFlag=0 then null else store_table.StoreGroupID end as StoreGroupID,
						StoreGroupName,
						StoreGroupNumber,
						case when @StoreFlag=0 then null else store_table.StoreID end as StoreID,
						store_table.StoreNumber as StoreNumber,
						store_table.StoreName as StoreName,
						store_table.StoreGCode as StoreGCode,
						ProfitCenterID,
						ProfitCenter,
						ReceiptClass,
						ReceiptClassID,
						ReceiptType,
						min(ReceiptTypeID) as ReceiptTypeID,
						CreditCardBatchNumber,
						CashierStaffDepartmentID,
						CashierStaffDepartmentName,
						CashierStaffID,
						CashierName,
						ServerStaffDepartmentID,
						ServerStaffDepartmentName,
						ServerStaffID,
						ServerName,
						CheckNumberID,
						CheckNumber,
						DayNameID,
						DayName,
						WeekNameID,
						WeekName,
						CashierSessionNumber,
						DayHourNumber,
						case when sum(Preliminary1)<>0 then 1 else 0 end as Preliminary1,
						case when sum(Preliminary2)<>0 then 1 else 0 end as Preliminary2,
						case when sum(Preliminary3)<>0 then 1 else 0 end as Preliminary3,
						case when sum(Preliminary4)<>0 then 1 else 0 end as Preliminary4,
						case when sum(Preliminary5)<>0 then 1 else 0 end as Preliminary5,
						case when sum(Preliminary6)<>0 then 1 else 0 end as Preliminary6,
						case when sum(Preliminary7)<>0 then 1 else 0 end as Preliminary7,
						sum(R1) as R1,sum(R2) as R2,sum(R3) as R3,sum(R4) as R4,sum(R5) as R5,sum(R6) as R6,sum(R7) as R7,
						sum(C1) as C1,sum(C2) as C2,sum(C3) as C3,sum(C4) as C4,sum(C5) as C5,sum(C6) as C6,sum(C7) as C7,
						sum(V1) as V1,sum(V2) as V2,sum(V3) as V3,sum(V4) as V4,sum(V5) as V5,sum(V6) as V6,sum(V7) as V7,
						sum(D1) as D1,sum(D2) as D2,sum(D3) as D3,sum(D4) as D4,sum(D5) as D5,sum(D6) as D6,sum(D7) as D7,
						sum(DepositCash1) as DepositCash1,sum(DepositCash2) as DepositCash2,sum(DepositCash3) as DepositCash3,sum(DepositCash4) as DepositCash4,sum(DepositCash5) as DepositCash5,sum(DepositCash6) as DepositCash6,sum(DepositCash7) as DepositCash7,
						sum(DepositCreditCard1) as DepositCreditCard1,sum(DepositCreditCard2) as DepositCreditCard2,sum(DepositCreditCard3) as DepositCreditCard3,sum(DepositCreditCard4) as DepositCreditCard4,sum(DepositCreditCard5) as DepositCreditCard5,sum(DepositCreditCard6) as DepositCreditCard6,sum(DepositCreditCard7) as DepositCreditCard7,
						sum(DepositCreditCardVISA1) as DepositCreditCardVISA1,sum(DepositCreditCardVISA2) as DepositCreditCardVISA2,sum(DepositCreditCardVISA3) as DepositCreditCardVISA3,sum(DepositCreditCardVISA4) as DepositCreditCardVISA4,sum(DepositCreditCardVISA5) as DepositCreditCardVISA5,sum(DepositCreditCardVISA6) as DepositCreditCardVISA6,sum(DepositCreditCardVISA7) as DepositCreditCardVISA7,
						sum(DepositCreditCardMC1) as DepositCreditCardMC1,sum(DepositCreditCardMC2) as DepositCreditCardMC2,sum(DepositCreditCardMC3) as DepositCreditCardMC3,sum(DepositCreditCardMC4) as DepositCreditCardMC4,sum(DepositCreditCardMC5) as DepositCreditCardMC5,sum(DepositCreditCardMC6) as DepositCreditCardMC6,sum(DepositCreditCardMC7) as DepositCreditCardMC7,
						sum(DepositCreditCardSwiped1) as DepositCreditCardSwiped1,sum(DepositCreditCardSwiped2) as DepositCreditCardSwiped2,sum(DepositCreditCardSwiped3) as DepositCreditCardSwiped3,sum(DepositCreditCardSwiped4) as DepositCreditCardSwiped4,sum(DepositCreditCardSwiped5) as DepositCreditCardSwiped5,sum(DepositCreditCardSwiped6) as DepositCreditCardSwiped6,sum(DepositCreditCardSwiped7) as DepositCreditCardSwiped7,
						sum(DepositCreditCardDiscover1) as DepositCreditCardDiscover1,sum(DepositCreditCardDiscover2) as DepositCreditCardDiscover2,sum(DepositCreditCardDiscover3) as DepositCreditCardDiscover3,sum(DepositCreditCardDiscover4) as DepositCreditCardDiscover4,sum(DepositCreditCardDiscover5) as DepositCreditCardDiscover5,sum(DepositCreditCardDiscover6) as DepositCreditCardDiscover6,sum(DepositCreditCardDiscover7) as DepositCreditCardDiscover7,
						sum(DepositCreditCardOther1) as DepositCreditCardOther1,sum(DepositCreditCardOther2) as DepositCreditCardOther2,sum(DepositCreditCardOther3) as DepositCreditCardOther3,sum(DepositCreditCardOther4) as DepositCreditCardOther4,sum(DepositCreditCardOther5) as DepositCreditCardOther5,sum(DepositCreditCardOther6) as DepositCreditCardOther6,sum(DepositCreditCardOther7) as DepositCreditCardOther7,
						sum(DepositCreditCardEMV1) as DepositCreditCardEMV1,sum(DepositCreditCardEMV2) as DepositCreditCardEMV2,sum(DepositCreditCardEMV3) as DepositCreditCardEMV3,sum(DepositCreditCardEMV4) as DepositCreditCardEMV4,sum(DepositCreditCardEMV5) as DepositCreditCardEMV5,sum(DepositCreditCardEMV6) as DepositCreditCardEMV6,sum(DepositCreditCardEMV7) as DepositCreditCardEMV7,
						sum(DepositCreditCardOLO1) as DepositCreditCardOLO1,sum(DepositCreditCardOLO2) as DepositCreditCardOLO2,sum(DepositCreditCardOLO3) as DepositCreditCardOLO3,sum(DepositCreditCardOLO4) as DepositCreditCardOLO4,sum(DepositCreditCardOLO5) as DepositCreditCardOLO5,sum(DepositCreditCardOLO6) as DepositCreditCardOLO6,sum(DepositCreditCardOLO7) as DepositCreditCardOLO7,
						sum(DepositCreditCardAmEx1) as DepositCreditCardAmEx1,sum(DepositCreditCardAmEx2) as DepositCreditCardAmEx2,sum(DepositCreditCardAmEx3) as DepositCreditCardAmEx3,sum(DepositCreditCardAmEx4) as DepositCreditCardAmEx4,sum(DepositCreditCardAmEx5) as DepositCreditCardAmEx5,sum(DepositCreditCardAmEx6) as DepositCreditCardAmEx6,sum(DepositCreditCardAmEx7) as DepositCreditCardAmEx7,
						sum(GiftCardRedeemedAmount1) as GiftCardRedeemedAmount1,sum(GiftCardRedeemedAmount2) as GiftCardRedeemedAmount2,sum(GiftCardRedeemedAmount3) as GiftCardRedeemedAmount3,sum(GiftCardRedeemedAmount4) as GiftCardRedeemedAmount4,sum(GiftCardRedeemedAmount5) as GiftCardRedeemedAmount5,sum(GiftCardRedeemedAmount6) as GiftCardRedeemedAmount6,sum(GiftCardRedeemedAmount7) as GiftCardRedeemedAmount7,
						sum(ND1) as D1,sum(ND2) as D2,sum(ND3) as D3,sum(ND4) as D4,sum(ND5) as D5,sum(ND6) as D6,sum(ND7) as D7,
						sum(Q1) as Q1,sum(Q2) as Q2,sum(Q3) as Q3,sum(Q4) as Q4,sum(Q5) as Q5,sum(Q6) as Q6,sum(Q7) as Q7,
						sum(CheckCount1) as CheckCount1,sum(CheckCount2) as CheckCount2,sum(CheckCount3) as CheckCount3,sum(CheckCount4) as CheckCount4,sum(CheckCount5) as CheckCount5,sum(CheckCount6) as CheckCount6,sum(CheckCount7) as CheckCount7,
						sum(DeclaredCoverCount1) as DeclaredCoverCount1,sum(DeclaredCoverCount2) as DeclaredCoverCount2,sum(DeclaredCoverCount3) as DeclaredCoverCount3,sum(DeclaredCoverCount4) as DeclaredCoverCount4,sum(DeclaredCoverCount5) as DeclaredCoverCount5,sum(DeclaredCoverCount6) as DeclaredCoverCount6,sum(DeclaredCoverCount7) as DeclaredCoverCount7,
						sum(ReceiptAmount1) as ReceiptAmount1,sum(ReceiptAmount2) as ReceiptAmount2,sum(ReceiptAmount3) as ReceiptAmount3,sum(ReceiptAmount4) as ReceiptAmount4,sum(ReceiptAmount5) as ReceiptAmount5,sum(ReceiptAmount6) as ReceiptAmount6,sum(ReceiptAmount7) as ReceiptAmount7,
						sum(TipsPaidAmount1) as TipsPaidAmount1, sum(TipsPaidAmount2) as TipsPaidAmount2, sum(TipsPaidAmount3) as TipsPaidAmount3,sum(TipsPaidAmount4) as TipsPaidAmount4,sum(TipsPaidAmount5) as TipsPaidAmount5,sum(TipsPaidAmount6) as TipsPaidAmount6,sum(TipsPaidAmount7) as TipsPaidAmount7,
						sum(TipsWithheldAmount1) as TipsWithheldAmount1, sum(TipsWithheldAmount2) as TipsWithheldAmount2, sum(TipsWithheldAmount3) as TipsWithheldAmount3,sum(TipsWithheldAmount4) as TipsWithheldAmount4,sum(TipsWithheldAmount5) as TipsWithheldAmount5,sum(TipsWithheldAmount6) as TipsWithheldAmount6,sum(TipsWithheldAmount7) as TipsWithheldAmount7,
						sum(GrossReceiptAmount1) as GrossReceiptAmount1,sum(GrossReceiptAmount2) as GrossReceiptAmount2,sum(GrossReceiptAmount3) as GrossReceiptAmount3,sum(GrossReceiptAmount4) as GrossReceiptAmount4,sum(GrossReceiptAmount5) as GrossReceiptAmount5,sum(GrossReceiptAmount6) as GrossReceiptAmount6,sum(GrossReceiptAmount7) as GrossReceiptAmount7,
						sum(TaxAmount1) as TaxAmount1, sum(TaxAmount2) as TaxAmount2, sum(TaxAmount3) as TaxAmount3,sum(TaxAmount4) as TaxAmount4,sum(TaxAmount5) as TaxAmount5,sum(TaxAmount6) as TaxAmount6,sum(TaxAmount7) as TaxAmount7,
						sum(ServerCommissionAmount1) as ServerCommissionAmount1,sum(ServerCommissionAmount2) as ServerCommissionAmount2,sum(ServerCommissionAmount3) as ServerCommissionAmount3,sum(ServerCommissionAmount4) as ServerCommissionAmount4,sum(ServerCommissionAmount5) as ServerCommissionAmount5,sum(ServerCommissionAmount6) as ServerCommissionAmount6,sum(ServerCommissionAmount7) as ServerCommissionAmount7,
						sum(GiftCardSoldAmount1) as GiftCardSoldAmount1,sum(GiftCardSoldAmount2) as GiftCardSoldAmount2,sum(GiftCardSoldAmount3) as GiftCardSoldAmount3,sum(GiftCardSoldAmount4) as GiftCardSoldAmount4,sum(GiftCardSoldAmount5) as GiftCardSoldAmount5,sum(GiftCardSoldAmount6) as GiftCardSoldAmount6,sum(GiftCardSoldAmount7) as GiftCardSoldAmount7,
						sum(ARPaidInAmount1) as ARPaidInAmount1,sum(ARPaidInAmount2) as ARPaidInAmount2,sum(ARPaidInAmount3) as ARPaidInAmount3,sum(ARPaidInAmount4) as ARPaidInAmount4,sum(ARPaidInAmount5) as ARPaidInAmount5,sum(ARPaidInAmount6) as ARPaidInAmount6,sum(ARPaidInAmount7) as ARPaidInAmount7,
						sum(PaidOutAmount1) as PaidOutAmount1,sum(PaidOutAmount2) as PaidOutAmount2,sum(PaidOutAmount3) as PaidOutAmount3,sum(PaidOutAmount4) as PaidOutAmount4,sum(PaidOutAmount5) as PaidOutAmount5,sum(PaidOutAmount6) as PaidOutAmount6,sum(PaidOutAmount7) as PaidOutAmount7,
						sum(-PaidOutAmount1) as PaidOutAmount1N,sum(-PaidOutAmount2) as PaidOutAmount2N,sum(-PaidOutAmount3) as PaidOutAmount3N,sum(-PaidOutAmount4) as PaidOutAmount4N,sum(-PaidOutAmount5) as PaidOutAmount5N,sum(-PaidOutAmount6) as PaidOutAmount6N,sum(-PaidOutAmount7) as PaidOutAmount7N,
						sum(ChargeAmount1) as ChargeAmount1,sum(ChargeAmount2) as ChargeAmount2,sum(ChargeAmount3) as ChargeAmount3,sum(ChargeAmount4) as ChargeAmount4,sum(ChargeAmount5) as ChargeAmount5,sum(ChargeAmount6) as ChargeAmount6,sum(ChargeAmount7) as ChargeAmount7,
						sum(NetReceiptAmount1) as NetReceiptAmount1,sum(NetReceiptAmount2) as NetReceiptAmount2,sum(NetReceiptAmount3) as NetReceiptAmount3,sum(NetReceiptAmount4) as NetReceiptAmount4,sum(NetReceiptAmount5) as NetReceiptAmount5,sum(NetReceiptAmount6) as NetReceiptAmount6,sum(NetReceiptAmount7) as NetReceiptAmount7,
						sum(DiscountAmount1) as DiscountAmount1,sum(DiscountAmount2) as DiscountAmount2,sum(DiscountAmount3) as DiscountAmount3,sum(DiscountAmount4) as DiscountAmount4,sum(DiscountAmount5) as DiscountAmount5,sum(DiscountAmount6) as DiscountAmount6,sum(DiscountAmount7) as DiscountAmount7,
						sum(TipsReceivedAmount1) as TipsReceivedAmount1, sum(TipsReceivedAmount2) as TipsReceivedAmount2, sum(TipsReceivedAmount3) as TipsReceivedAmount3,sum(TipsReceivedAmount4) as TipsReceivedAmount4,sum(TipsReceivedAmount5) as TipsReceivedAmount5,sum(TipsReceivedAmount6) as TipsReceivedAmount6,sum(TipsReceivedAmount7) as TipsReceivedAmount7,
						sum(TipsDeclaredAmount1) as TipsDeclaredAmount1, sum(TipsDeclaredAmount2) as TipsDeclaredAmount2, sum(TipsDeclaredAmount3) as TipsDeclaredAmount3,sum(TipsDeclaredAmount4) as TipsDeclaredAmount4,sum(TipsDeclaredAmount5) as TipsDeclaredAmount5,sum(TipsDeclaredAmount6) as TipsDeclaredAmount6,sum(TipsDeclaredAmount7) as TipsDeclaredAmount7,
						sum(PayrollSalesAmount1) as PayrollSalesAmount1, sum(PayrollSalesAmount2) as PayrollSalesAmount2, sum(PayrollSalesAmount3) as PayrollSalesAmount3,sum(PayrollSalesAmount4) as PayrollSalesAmount4,sum(PayrollSalesAmount5) as PayrollSalesAmount5,sum(PayrollSalesAmount6) as PayrollSalesAmount6,sum(PayrollSalesAmount7) as PayrollSalesAmount7,
						sum(CreditCardSalesAmount1) as CreditCardSalesAmount1, sum(CreditCardSalesAmount2) as CreditCardSalesAmount2, sum(CreditCardSalesAmount3) as CreditCardSalesAmount3,sum(CreditCardSalesAmount4) as CreditCardSalesAmount4,sum(CreditCardSalesAmount5) as CreditCardSalesAmount5,sum(CreditCardSalesAmount6) as CreditCardSalesAmount6,sum(CreditCardSalesAmount7) as CreditCardSalesAmount7,
						sum(CreditCardTipAmount1) as CreditCardTipAmount1, sum(CreditCardTipAmount2) as CreditCardTipAmount2, sum(CreditCardTipAmount3) as CreditCardTipAmount3,sum(CreditCardTipAmount4) as CreditCardTipAmount4,sum(CreditCardTipAmount5) as CreditCardTipAmount5,sum(CreditCardTipAmount6) as CreditCardTipAmount6,sum(CreditCardTipAmount7) as CreditCardTipAmount7,
						sum(TaxableSales1) as TaxableSales1, sum(TaxableSales2) as TaxableSales2, sum(TaxableSales3) as TaxableSales3,sum(TaxableSales4) as TaxableSales4,sum(TaxableSales5) as TaxableSales5,sum(TaxableSales6) as TaxableSales6,sum(TaxableSales7) as TaxableSales7,
						sum(NonTaxableSales1) as NonTaxableSales1, sum(NonTaxableSales2) as NonTaxableSales2, sum(NonTaxableSales3) as NonTaxableSales3,sum(NonTaxableSales4) as NonTaxableSales4,sum(NonTaxableSales5) as NonTaxableSales5,sum(NonTaxableSales6) as NonTaxableSales6,sum(NonTaxableSales7) as NonTaxableSales7,
						sum(BadPayments1) as BadPayments1, sum(BadPayments2) as BadPayments2, sum(BadPayments3) as BadPayments3,sum(BadPayments4) as BadPayments4,sum(BadPayments5) as BadPayments5,sum(BadPayments6) as BadPayments6,sum(BadPayments7) as BadPayments7,
						sum(TotalReceipts) AS TotalReceipts,
						sum(TotalOverShort) as TotalOverShort,
						min(TotalOverShort) as TotalOverShortMin,
						sum(NetReceipts) as NetReceipts,
						sum(TotalDeposit) as TotalDeposit,
						sum(TotalDepositCash) as TotalDepositCash,
						sum(TotalDepositCreditCard) as TotalDepositCreditCard,
						sum(TotalDepositCreditCardVISA) as TotalDepositCreditCardVISA,
						sum(TotalDepositCreditCardMC) as TotalDepositCreditCardMC,
						sum(TotalDepositCreditCardSwiped) as TotalDepositCreditCardSwiped,
						sum(TotalDepositCreditCardDiscover) as TotalDepositCreditCardDiscover,
						sum(TotalDepositCreditCardOther) as TotalDepositCreditCardOther,
						sum(TotalDepositCreditCardEMV) as TotalDepositCreditCardEMV,
						sum(TotalDepositCreditCardOLO) as TotalDepositCreditCardOLO,
						sum(TotalDepositCreditCardAmEx) as TotalDepositCreditCardAmEx,
						sum(TotalGiftCardRedeemed) as TotalGiftCardRedeemed,
						sum(TotalOtherNonDeposit) as TotalOtherNonDeposit,
						sum(TotalCount) as TotalCount,
						sum(TotalCheckCount) as TotalCheckCount,
						sum(TotalDeclaredCoverCount) as TotalDeclaredCoverCount,
						sum(TotalReceiptAmount) as TotalReceiptAmount,
						sum(TotalTipsPaidAmount) as TotalTipsPaidAmount,
						sum(TotalTipsWithheldAmount) as TotalTipsWithheldAmount,
						sum(TotalGrossReceiptAmount) as TotalGrossReceiptAmount,
						sum(TotalTaxAmount) as TotalTaxAmount,
						sum(TotalServerCommissionAmount) as TotalServerCommissionAmount,
						sum(TotalGiftCardSoldAmount) as TotalGiftCardSoldAmount,
						sum(TotalARPaidInAmount) as TotalARPaidInAmount,
						sum(TotalPaidOutAmount) as TotalPaidOutAmount,
						sum(-TotalPaidOutAmount) as TotalPaidOutAmountN,
						sum(TotalGrossReceiptAmount-TotalPaidOutAmount) as TotalGrossRevenueAmount,
						sum(TotalChargeAmount) as TotalChargeAmount,
						sum(TotalNetReceiptAmount) as TotalNetReceiptAmount,
						sum(TotalDiscountAmount) as TotalDiscountAmount,
						sum(TotalTipsReceivedAmount) as TotalTipsReceivedAmount,
						sum(TotalTipsDeclaredAmount) as TotalTipsDeclaredAmount,
						sum(TotalPayrollSalesAmount) as TotalPayrollSalesAmount,
						sum(TotalFood) as TotalFood,
						sum(TotalBeverage) as TotalBeverage,
						sum(TotalOther) as TotalOther,
						sum(TotalDiscount) as TotalDiscount,
						sum(TotalTax) as TotalTax,
						sum(TotalCreditCardSales) as TotalCreditCardSales,
						sum(TotalCreditCardTip) as TotalCreditCardTip,
						sum(TotalTaxableSales) as TotalTaxableSales,
						sum(TotalNonTaxableSales) as TotalNonTaxableSales,
						sum(TotalBadPayments) as TotalBadPayments
					from
						@store_table store_table
					group by
						case when @StoreGroupFlag=0 then null else store_table.StoreGroupID end,
						StoreGroupName,
						StoreGroupNumber,
						case when @StoreFlag=0 then null else store_table.StoreID end,
						store_table.StoreNumber,
						store_table.StoreName,
						store_table.StoreGCode,
						ProfitCenterID,
						ProfitCenter,
						ReceiptClass,
						ReceiptClassID,
						ReceiptType,
						ReceiptTypeID,
						CreditCardBatchNumber,
						CashierStaffDepartmentID,
						CashierStaffDepartmentName,
						CashierStaffID,
						CashierName,
						ServerStaffDepartmentID,
						ServerStaffDepartmentName,
						ServerStaffID,
						ServerName,
						CheckNumberID,
						CheckNumber,
						DayNameID,
						DayName,
						WeekNameID,
						WeekName,
						CashierSessionNumber,
						DayHourNumber
					order by StoreNumber, WeekNameID, DayNameID, case when ReceiptTypeID=1 then 0 else 1 end, TotalGrossReceiptAmount desc, TotalDiscountAmount desc, CashierName, ServerName, CheckNumberID, TotalOverShort desc
					update @table_variable set
						TotalSales1=NetReceiptAmount1+DiscountAmount1,
						TotalSales2=NetReceiptAmount2+DiscountAmount2,
						TotalSales3=NetReceiptAmount3+DiscountAmount3,
						TotalSales4=NetReceiptAmount4+DiscountAmount4,
						TotalSales5=NetReceiptAmount5+DiscountAmount5,
						TotalSales6=NetReceiptAmount6+DiscountAmount6,
						TotalSales7=NetReceiptAmount7+DiscountAmount7,
						TipsAmount1=TipsReceivedAmount1+TipsDeclaredAmount1,
						TipsAmount2=TipsReceivedAmount2+TipsDeclaredAmount2,
						TipsAmount3=TipsReceivedAmount3+TipsDeclaredAmount3,
						TipsAmount4=TipsReceivedAmount4+TipsDeclaredAmount4,
						TipsAmount5=TipsReceivedAmount5+TipsDeclaredAmount5,
						TipsAmount6=TipsReceivedAmount6+TipsDeclaredAmount6,
						TipsAmount7=TipsReceivedAmount7+TipsDeclaredAmount7,
						TotalSalesAmount=TotalNetReceiptAmount+TotalDiscountAmount,
						TotalTipsAmount=TotalTipsReceivedAmount+TotalTipsDeclaredAmount
					update @table_variable set
						TotalDiscountPercentage=case when isnull(TotalSalesAmount,0)<>0 then isnull(TotalDiscountAmount,0)/isnull(TotalSalesAmount,0) else 0 end,
						TotalTipsVsNetPercentageAmount=case when TotalNetReceiptAmount<>0 then TotalTipsAmount/TotalNetReceiptAmount else 0 end,
						AverageNetSalesPerPayment1 = case when Q1<>0 then NetReceiptAmount1/Q1 else 0 end,
						AverageNetSalesPerPayment2 = case when Q2<>0 then NetReceiptAmount2/Q2 else 0 end,
						AverageNetSalesPerPayment3 = case when Q3<>0 then NetReceiptAmount3/Q3 else 0 end,
						AverageNetSalesPerPayment4 = case when Q4<>0 then NetReceiptAmount4/Q4 else 0 end,
						AverageNetSalesPerPayment5 = case when Q5<>0 then NetReceiptAmount5/Q5 else 0 end,
						AverageNetSalesPerPayment6 = case when Q6<>0 then NetReceiptAmount6/Q6 else 0 end,
						AverageNetSalesPerPayment7 = case when Q7<>0 then NetReceiptAmount7/Q7 else 0 end,
						AverageNetSalesPerPayment = case when TotalCount<>0 then TotalNetReceiptAmount/TotalCount else 0 end,
						AverageTipsPerPayment1 = case when Q1<>0 then TipsAmount1/Q1 else 0 end,
						AverageTipsPerPayment2 = case when Q2<>0 then TipsAmount2/Q2 else 0 end,
						AverageTipsPerPayment3 = case when Q3<>0 then TipsAmount3/Q3 else 0 end,
						AverageTipsPerPayment4 = case when Q4<>0 then TipsAmount4/Q4 else 0 end,
						AverageTipsPerPayment5 = case when Q5<>0 then TipsAmount5/Q5 else 0 end,
						AverageTipsPerPayment6 = case when Q6<>0 then TipsAmount6/Q6 else 0 end,
						AverageTipsPerPayment7 = case when Q7<>0 then TipsAmount7/Q7 else 0 end,
						AverageTipsPerPayment = case when TotalCount<>0 then TotalTipsAmount/TotalCount else 0 end
					update @table_variable set DayHourName=dbo.fn_DayHourName(DayHourNumber) where not DayHourNumber is null
					select *");
					foreach (KeyValuePair<String,String> kvp in rv.CalculatedFields) {
						sb.Append(", " + kvp.Value + " as " + kvp.Key);
						}
					sb.Append(" from @table_variable tbl\n");
				return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
				}
			public ReceiptsSelect(DataAccessReceiptsReview rv) {
				this.rv = rv; /* all dimensions included in all output, subqueries have already nulled out non selected dimensions */
				}
			}
		private class ReceiptsOuterSelect : Select {
			public Int32 ReceiptDetailsCashTipsEarnedID = 0;
			DataAccessReceiptsReview rv;
			public override String OutputSelect(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				sb.Append(@"
					select
						PostingDate as StartingDate,
						PostingDate as EndingDate,
						Preliminary as Preliminary" + 
						OutputSelectColumns(Select.CommaModes.BeforeAndAfter, TabCount+1) + @"
						case when sum(Preliminary1)<>0 then 1 else 0 end as Preliminary1,
						case when sum(Preliminary2)<>0 then 1 else 0 end as Preliminary2,
						case when sum(Preliminary3)<>0 then 1 else 0 end as Preliminary3,
						case when sum(Preliminary4)<>0 then 1 else 0 end as Preliminary4,
						case when sum(Preliminary5)<>0 then 1 else 0 end as Preliminary5,
						case when sum(Preliminary6)<>0 then 1 else 0 end as Preliminary6,
						case when sum(Preliminary7)<>0 then 1 else 0 end as Preliminary7,
						R1, R2, R3, R4, R5, R6, R7,
						C1, C2, C3, C4, C5, C6, C7,
						V1, V2, V3, V4, V5, V6, V7,
						D1, D2, D3, D4, D5, D6, D7,
						DepositCash1,               DepositCash2,               DepositCash3,               DepositCash4,               DepositCash5,               DepositCash6,               DepositCash7,
						DepositCreditCard1,         DepositCreditCard2,         DepositCreditCard3,         DepositCreditCard4,         DepositCreditCard5,         DepositCreditCard6,         DepositCreditCard7,
						DepositCreditCardVISA1,     DepositCreditCardVISA2,     DepositCreditCardVISA3,     DepositCreditCardVISA4,     DepositCreditCardVISA5,     DepositCreditCardVISA6,     DepositCreditCardVISA7,
						DepositCreditCardMC1,       DepositCreditCardMC2,       DepositCreditCardMC3,       DepositCreditCardMC4,       DepositCreditCardMC5,       DepositCreditCardMC6,       DepositCreditCardMC7,
						DepositCreditCardSwiped1,   DepositCreditCardSwiped2,   DepositCreditCardSwiped3,   DepositCreditCardSwiped4,   DepositCreditCardSwiped5,   DepositCreditCardSwiped6,   DepositCreditCardSwiped7,
						DepositCreditCardDiscover1, DepositCreditCardDiscover2, DepositCreditCardDiscover3, DepositCreditCardDiscover4, DepositCreditCardDiscover5, DepositCreditCardDiscover6, DepositCreditCardDiscover7,
						DepositCreditCardOther1, DepositCreditCardOther2, DepositCreditCardOther3, DepositCreditCardOther4, DepositCreditCardOther5, DepositCreditCardOther6, DepositCreditCardOther7,
						DepositCreditCardEMV1,      DepositCreditCardEMV2,      DepositCreditCardEMV3,      DepositCreditCardEMV4,      DepositCreditCardEMV5,      DepositCreditCardEMV6,      DepositCreditCardEMV7,
						DepositCreditCardOLO1,      DepositCreditCardOLO2,      DepositCreditCardOLO3,      DepositCreditCardOLO4,      DepositCreditCardOLO5,      DepositCreditCardOLO6,      DepositCreditCardOLO7,
						DepositCreditCardAmEx1,     DepositCreditCardAmEx2,     DepositCreditCardAmEx3,     DepositCreditCardAmEx4,     DepositCreditCardAmEx5,     DepositCreditCardAmEx6,     DepositCreditCardAmEx7,
						GiftCardRedeemedAmount1, GiftCardRedeemedAmount2, GiftCardRedeemedAmount3, GiftCardRedeemedAmount4, GiftCardRedeemedAmount5, GiftCardRedeemedAmount6, GiftCardRedeemedAmount7,
						C1-D1-GiftCardRedeemedAmount1, C2-D2-GiftCardRedeemedAmount2, C3-D3-GiftCardRedeemedAmount3, C4-D4-GiftCardRedeemedAmount4, C5-D5-GiftCardRedeemedAmount5, C6-D6-GiftCardRedeemedAmount6, C7-D7-GiftCardRedeemedAmount7,
						sum(Q1) as Q1, sum(Q2) as Q2, sum(Q3) as Q3, sum(Q4) as Q4, sum(Q5) as Q5, sum(Q6) as Q6, sum(Q7) as Q7,
						sum(CheckCount1) as CheckCount1, sum(CheckCount2) as CheckCount2, sum(CheckCount3) as CheckCount3, sum(CheckCount4) as CheckCount4, sum(CheckCount5) as CheckCount5, sum(CheckCount6) as CheckCount6, sum(CheckCount7) as CheckCount7,
						sum(DeclaredCoverCount1) as DeclaredCoverCount1, sum(DeclaredCoverCount2) as DeclaredCoverCount2, sum(DeclaredCoverCount3) as DeclaredCoverCount3, sum(DeclaredCoverCount4) as DeclaredCoverCount4, sum(DeclaredCoverCount5) as DeclaredCoverCount5, sum(DeclaredCoverCount6) as DeclaredCoverCount6, sum(DeclaredCoverCount7) as DeclaredCoverCount7,
						sum(ReceiptAmount1) as ReceiptAmount1, sum(ReceiptAmount2) as ReceiptAmount2, sum(ReceiptAmount3) as ReceiptAmount3, sum(ReceiptAmount4) as ReceiptAmount4, sum(ReceiptAmount5) as ReceiptAmount5, sum(ReceiptAmount6) as ReceiptAmount6, sum(ReceiptAmount7) as ReceiptAmount7,
						sum(TipsPaidAmount1) as TipsPaidAmount1, sum(TipsPaidAmount2) as TipsPaidAmount2, sum(TipsPaidAmount3) as TipsPaidAmount3, sum(TipsPaidAmount4) as TipsPaidAmount4, sum(TipsPaidAmount5) as TipsPaidAmount5, sum(TipsPaidAmount6) as TipsPaidAmount6, sum(TipsPaidAmount7) as TipsPaidAmount7,
						sum(TipsWithheldAmount1) as TipsWithheldAmount1, sum(TipsWithheldAmount2) as TipsWithheldAmount2, sum(TipsWithheldAmount3) as TipsWithheldAmount3, sum(TipsWithheldAmount4) as TipsWithheldAmount4, sum(TipsWithheldAmount5) as TipsWithheldAmount5, sum(TipsWithheldAmount6) as TipsWithheldAmount6, sum(TipsWithheldAmount7) as TipsWithheldAmount7,
						sum(GrossReceiptAmount1) as GrossReceiptAmount1, sum(GrossReceiptAmount2) as GrossReceiptAmount2, sum(GrossReceiptAmount3) as GrossReceiptAmount3, sum(GrossReceiptAmount4) as GrossReceiptAmount4, sum(GrossReceiptAmount5) as GrossReceiptAmount5, sum(GrossReceiptAmount6) as GrossReceiptAmount6, sum(GrossReceiptAmount7) as GrossReceiptAmount7,
						sum(TaxAmount1) as TaxAmount1, sum(TaxAmount2) as TaxAmount2, sum(TaxAmount3) as TaxAmount3, sum(TaxAmount4) as TaxAmount4, sum(TaxAmount5) as TaxAmount5, sum(TaxAmount6) as TaxAmount6, sum(TaxAmount7) as TaxAmount7,
						sum(ServerCommissionAmount1) as ServerCommissionAmount1, sum(ServerCommissionAmount2) as ServerCommissionAmount2, sum(ServerCommissionAmount3) as ServerCommissionAmount3, sum(ServerCommissionAmount4) as ServerCommissionAmount4, sum(ServerCommissionAmount5) as ServerCommissionAmount5, sum(ServerCommissionAmount6) as ServerCommissionAmount6, sum(ServerCommissionAmount7) as ServerCommissionAmount7,
						sum(GiftCardSoldAmount1) as GiftCardSoldAmount1, sum(GiftCardSoldAmount2) as GiftCardSoldAmount2, sum(GiftCardSoldAmount3) as GiftCardSoldAmount3, sum(GiftCardSoldAmount4) as GiftCardSoldAmount4, sum(GiftCardSoldAmount5) as GiftCardSoldAmount5, sum(GiftCardSoldAmount6) as GiftCardSoldAmount6, sum(GiftCardSoldAmount7) as GiftCardSoldAmount7,
						sum(ARPaidInAmount1) as ARPaidInAmount1, sum(ARPaidInAmount2) as ARPaidInAmount2, sum(ARPaidInAmount3) as ARPaidInAmount3, sum(ARPaidInAmount4) as ARPaidInAmount4, sum(ARPaidInAmount5) as ARPaidInAmount5, sum(ARPaidInAmount6) as ARPaidInAmount6, sum(ARPaidInAmount7) as ARPaidInAmount7,
						sum(PaidOutAmount1) as PaidOutAmount1, sum(PaidOutAmount2) as PaidOutAmount2, sum(PaidOutAmount3) as PaidOutAmount3, sum(PaidOutAmount4) as PaidOutAmount4, sum(PaidOutAmount5) as PaidOutAmount5, sum(PaidOutAmount6) as PaidOutAmount6, sum(PaidOutAmount7) as PaidOutAmount7,
						sum(ChargeAmount1) as ChargeAmount1, sum(ChargeAmount2) as ChargeAmount2, sum(ChargeAmount3) as ChargeAmount3, sum(ChargeAmount4) as ChargeAmount4, sum(ChargeAmount5) as ChargeAmount5, sum(ChargeAmount6) as ChargeAmount6, sum(ChargeAmount7) as ChargeAmount7,
						sum(NetReceiptAmount1) as NetReceiptAmount1, sum(NetReceiptAmount2) as NetReceiptAmount2, sum(NetReceiptAmount3) as NetReceiptAmount3, sum(NetReceiptAmount4) as NetReceiptAmount4, sum(NetReceiptAmount5) as NetReceiptAmount5, sum(NetReceiptAmount6) as NetReceiptAmount6, sum(NetReceiptAmount7) as NetReceiptAmount7,
						sum(DiscountAmount1) as DiscountAmount1, sum(DiscountAmount2) as DiscountAmount2, sum(DiscountAmount3) as DiscountAmount3, sum(DiscountAmount4) as DiscountAmount4, sum(DiscountAmount5) as DiscountAmount5, sum(DiscountAmount6) as DiscountAmount6, sum(DiscountAmount7) as DiscountAmount7,
						sum(TipsReceivedAmount1) as TipsReceivedAmount1, sum(TipsReceivedAmount2) as TipsReceivedAmount2, sum(TipsReceivedAmount3) as TipsReceivedAmount3, sum(TipsReceivedAmount4) as TipsReceivedAmount4, sum(TipsReceivedAmount5) as TipsReceivedAmount5, sum(TipsReceivedAmount6) as TipsReceivedAmount6, sum(TipsReceivedAmount7) as TipsReceivedAmount7,
						sum(TipsDeclaredAmount1) as TipsDeclaredAmount1, sum(TipsDeclaredAmount2) as TipsDeclaredAmount2, sum(TipsDeclaredAmount3) as TipsDeclaredAmount3, sum(TipsDeclaredAmount4) as TipsDeclaredAmount4, sum(TipsDeclaredAmount5) as TipsDeclaredAmount5, sum(TipsDeclaredAmount6) as TipsDeclaredAmount6, sum(TipsDeclaredAmount7) as TipsDeclaredAmount7,
						null as TipsAmount1, null as TipsAmount2, null as TipsAmount3, null as TipsAmount4, null as TipsAmount5, null as TipsAmount6, null as TipsAmount7,
						sum(PayrollSalesAmount1) as PayrollSalesAmount1, sum(PayrollSalesAmount2) as PayrollSalesAmount2, sum(PayrollSalesAmount3) as PayrollSalesAmount3, sum(PayrollSalesAmount4) as PayrollSalesAmount4, sum(PayrollSalesAmount5) as PayrollSalesAmount5, sum(PayrollSalesAmount6) as PayrollSalesAmount6, sum(PayrollSalesAmount7) as PayrollSalesAmount7,
						sum(CreditCardSalesAmount1) as CreditCardSalesAmount1, sum(CreditCardSalesAmount2) as CreditCardSalesAmount2, sum(CreditCardSalesAmount3) as CreditCardSalesAmount3, sum(CreditCardSalesAmount4) as CreditCardSalesAmount4, sum(CreditCardSalesAmount5) as CreditCardSalesAmount5, sum(CreditCardSalesAmount6) as CreditCardSalesAmount6, sum(CreditCardSalesAmount7) as CreditCardSalesAmount7,
						sum(CreditCardTipAmount1) as CreditCardTipAmount1, sum(CreditCardTipAmount2) as CreditCardTipAmount2, sum(CreditCardTipAmount3) as CreditCardTipAmount3, sum(CreditCardTipAmount4) as CreditCardTipAmount4, sum(CreditCardTipAmount5) as CreditCardTipAmount5, sum(CreditCardTipAmount6) as CreditCardTipAmount6, sum(CreditCardTipAmount7) as CreditCardTipAmount7,
						sum(TaxableSalesAmount1) as TaxableSalesAmount1, sum(TaxableSalesAmount2) as TaxableSalesAmount2, sum(TaxableSalesAmount3) as TaxableSalesAmount3, sum(TaxableSalesAmount4) as TaxableSalesAmount4, sum(TaxableSalesAmount5) as TaxableSalesAmount5, sum(TaxableSalesAmount6) as TaxableSalesAmount6, sum(TaxableSalesAmount7) as TaxableSalesAmount7,
						sum(NonTaxableSalesAmount1) as NonTaxableSalesAmount1, sum(NonTaxableSalesAmount2) as NonTaxableSalesAmount2, sum(NonTaxableSalesAmount3) as NonTaxableSalesAmount3, sum(NonTaxableSalesAmount4) as NonTaxableSalesAmount4, sum(NonTaxableSalesAmount5) as NonTaxableSalesAmount5, sum(NonTaxableSalesAmount6) as NonTaxableSalesAmount6, sum(NonTaxableSalesAmount7) as NonTaxableSalesAmount7,
						sum(BadPaymentsAmount1) as BadPaymentsAmount1, sum(BadPaymentsAmount2) as BadPaymentsAmount2, sum(BadPaymentsAmount3) as BadPaymentsAmount3, sum(BadPaymentsAmount4) as BadPaymentsAmount4, sum(BadPaymentsAmount5) as BadPaymentsAmount5, sum(BadPaymentsAmount6) as BadPaymentsAmount6, sum(BadPaymentsAmount7) as BadPaymentsAmount7,
						sum(TotalReceipts) as TotalReceipts,
						sum(TotalOverShort) as TotalOverShort,
						sum(NetReceipts) as NetReceipts,
						sum(TotalDeposit) as TotalDeposit,
						sum(TotalDepositCash) as TotalDepositCash,
						sum(TotalDepositCreditCard) as TotalDepositCreditCard,
						sum(TotalDepositCreditCardVISA) as TotalDepositCreditCardVISA,
						sum(TotalDepositCreditCardMC) as TotalDepositCreditCardMC,
						sum(TotalDepositCreditCardSwiped) as TotalDepositCreditCardSwiped,
						sum(TotalDepositCreditCardDiscover) as TotalDepositCreditCardDiscover,
						sum(TotalDepositCreditCardOther) as TotalDepositCreditCardOther,
						sum(TotalDepositCreditCardEMV) as TotalDepositCreditCardEMV,
						sum(TotalDepositCreditCardOLO) as TotalDepositCreditCardOLO,
						sum(TotalDepositCreditCardAmEx) as TotalDepositCreditCardAmEx,
						sum(TotalGiftCardRedeemed) as TotalGiftCardRedeemed,
						NetReceipts-TotalGiftCardRedeemed-TotalDeposit as TotalOtherNonDeposit,
						sum(ReceiptCount) as TotalCount,
						sum(CheckCount) as TotalCheckCount,
						sum(DeclaredCoverCount) as TotalDeclaredCoverCount,
						sum(ReceiptAmount) as TotalReceiptAmount,
						sum(TipsPaidAmount) as TotalTipsPaidAmount,
						sum(TipsWithheldAmount) as TotalTipsWithheldAmount,
						sum(GrossReceiptAmount) as TotalGrossReceiptAmount,
						sum(TaxAmount) as TotalTaxAmount,
						sum(ServerCommissionAmount) as TotalServerCommissionAmount,
						sum(GiftCardSoldAmount) as TotalGiftCardSoldAmount,
						sum(ARPaidInAmount) as TotalARPaidInAMountAmount,
						sum(PaidOutAmount) as TotalPaidOutAmount,
						sum(ChargeAmount) as TotalChargeAmount,
						sum(NetReceiptAmount) as TotalNetReceiptAmount,
						sum(DiscountAmount) as TotalDiscountAmount,
						sum(TipsReceivedAmount) as TotalTipsReceivedAmount,
						sum(TipsDeclaredAmount) as TotalTipsDeclaredAmount,
						null as TotalTipsAmount,
						sum(PayrollSalesAmount) as TotalPayrollSalesAmount,
						sum(AmountFood) as TotalFood,
						sum(AmountBeverage) as TotalBeverage,
						sum(AmountOther) as TotalOther,
						sum(AmountDiscount) as TotalDiscount,
						sum(AmountTax) as TotalTax,
						sum(AmountCreditCardSales) as AmountCreditCardSales,
						sum(AmountCreditCardTip) as AmountCreditCardTip,
						sum(TaxableSales) as TotalTaxableSales,
						sum(NonTaxableSales) as AmountNonTaxableSales,
						sum(BadPayments) as AmountBadPayments
					from (
						select
							css.PostingDate,
							css.Preliminary,
							case when @StoreGroupFlag=0 then null else tblStoreGroups.StoreGroupName end as StoreGroupName,
							case when @StoreGroupFlag=0 then null else tblStoreGroups.StoreGroupNumber end as StoreGroupNumber,
							case when @StoreGroupFlag=0 then null else tblStores.StoreGroupID end as StoreGroupID,
							case when @StoreFlag=0 and @CreditCardBatchNumberFlag=0 then null else isnull(tblStores.StoreReportName,tblStores.StoreName) end as StoreName,
							case when @StoreFlag=0 and @CreditCardBatchNumberFlag=0 then null else tblStores.StoreGCode end as StoreGCode,
							case when @StoreFlag=0 and @CreditCardBatchNumberFlag=0 then null else tblStores.StoreNumber end as StoreNumber,
							case when @StoreFlag=0 then null else css.StoreID end as StoreID,
							case when @ProfitCenterFlag=0 then null else tblProfitCenters.ProfitCenter end as ProfitCenter,
							case when @ProfitCenterFlag=0 then null else css.ProfitCenterID end as ProfitCenterID,
							case when @ReceiptClassFlag=0 then null else css.ReceiptClass end as ReceiptClass,
							case when @ReceiptClassFlag=0 then null else css.ReceiptClassID end as ReceiptClassID,
							case when @ReceiptTypeFlag=0 then null else css.ReceiptType end as ReceiptType,
							null as ReceiptTypeID, 
							case when @CreditCardBatchNumberFlag=0 then null else css.CreditCardBatchNumber end as CreditCardBatchNumber,
							null as CashierStaffDepartmentID,
							case when @CashierStaffDepartmentFlag=0 then null else css.CashierStaffDepartmentName end as CashierStaffDepartmentName,
							null as CashierStaffID,
							case when @CashierFlag=0 then null else css.CashierName end as CashierName,
							null as ServerStaffDepartmentID,
							case when @ServerStaffDepartmentFlag=0 then null else css.ServerStaffDepartmentName end as ServerStaffDepartmentName,
							null as ServerStaffID,
							case when @ServerFlag=0 then null else css.ServerName end as ServerName,
							case when @CheckNumberFlag=0 then null else css.CheckNumberID end as CheckNumberID,
							case when @CheckNumberFlag=0 then null else css.CheckNumber end as CheckNumber,
							case when @DayNameFlag=0 then null else css.DayNameID end as DayNameID,
							case when @DayNameFlag=0 then null else css.DayName end as DayName,
							case when @WeekNameFlag=0 then null else css.WeekNameID end as WeekNameID,
							case when @WeekNameFlag=0 then null else css.WeekName end as WeekName,
							case when @CashierSessionNumberFlag=0 then null else css.CashierSessionNumber end as CashierSessionNumber,
							case when @DayHourNumberFlag=0 then null else css.DayHourNumber end as DayHourNumber,
							css.Expected as TotalReceipts,
							css.Variance as TotalOverShort,
							isnull(css.Expected,0)+isnull(css.Variance,0) as NetReceipts,
							css.GiftCardRedeemedAmount as TotalGiftCardRedeemed,
							css.Deposit as TotalDeposit,
							css.DepositCash as TotalDepositCash,
							css.DepositCreditCard as TotalDepositCreditCard,
							css.DepositCreditCardVISA as TotalDepositCreditCardVISA,
							css.DepositCreditCardMC as TotalDepositCreditCardMC,
							css.DepositCreditCardSwiped as TotalDepositCreditCardSwiped,
							css.DepositCreditCardDiscover as TotalDepositCreditCardDiscover,
							css.DepositCreditCardOther as TotalDepositCreditCardOther,
							css.DepositCreditCardEMV as TotalDepositCreditCardEMV,
							css.DepositCreditCardOLO as TotalDepositCreditCardOLO,
							css.DepositCreditCardAmEx as TotalDepositCreditCardAmEx,
							css.ReceiptCount,
							css.CheckCount,
							css.DeclaredCoverCount,
							css.ReceiptAmount,
							css.TipsPaidAmount,
							css.TipsWithheldAmount,
							css.GrossReceiptAmount,
							css.TaxAmount,
							css.ChargeAmount,
							css.NetReceiptAmount,
							css.DiscountAmount,
							css.ServerCommissionAmount,
							css.GiftCardSoldAmount,
							css.ARPaidInAmount,
							css.GiftCardRedeemedAmount,
							css.PaidOutAmount,
							css.TipsReceivedAmount,
							css.TipsDeclaredAmount,
							css.PayrollSalesAmount,
							case when css.DayNumber%7+1 = 1 then Preliminary else 0 end as Preliminary1,
							case when css.DayNumber%7+1 = 2 then Preliminary else 0 end as Preliminary2,
							case when css.DayNumber%7+1 = 3 then Preliminary else 0 end as Preliminary3,
							case when css.DayNumber%7+1 = 4 then Preliminary else 0 end as Preliminary4,
							case when css.DayNumber%7+1 = 5 then Preliminary else 0 end as Preliminary5,
							case when css.DayNumber%7+1 = 6 then Preliminary else 0 end as Preliminary6,
							case when css.DayNumber%7+1 = 7 then Preliminary else 0 end as Preliminary7,
							case when css.DayNumber%7+1 = 1 then Expected else 0 end as R1,
							case when css.DayNumber%7+1 = 2 then Expected else 0 end as R2,
							case when css.DayNumber%7+1 = 3 then Expected else 0 end as R3,
							case when css.DayNumber%7+1 = 4 then Expected else 0 end as R4,
							case when css.DayNumber%7+1 = 5 then Expected else 0 end as R5,
							case when css.DayNumber%7+1 = 6 then Expected else 0 end as R6,
							case when css.DayNumber%7+1 = 7 then Expected else 0 end as R7,
							case when css.DayNumber%7+1 = 1 then Counted else 0 end as C1,
							case when css.DayNumber%7+1 = 2 then Counted else 0 end as C2,
							case when css.DayNumber%7+1 = 3 then Counted else 0 end as C3,
							case when css.DayNumber%7+1 = 4 then Counted else 0 end as C4,
							case when css.DayNumber%7+1 = 5 then Counted else 0 end as C5,
							case when css.DayNumber%7+1 = 6 then Counted else 0 end as C6,
							case when css.DayNumber%7+1 = 7 then Counted else 0 end as C7,
							case when css.DayNumber%7+1 = 1 then Variance else 0 end as V1,
							case when css.DayNumber%7+1 = 2 then Variance else 0 end as V2,
							case when css.DayNumber%7+1 = 3 then Variance else 0 end as V3,
							case when css.DayNumber%7+1 = 4 then Variance else 0 end as V4,
							case when css.DayNumber%7+1 = 5 then Variance else 0 end as V5,
							case when css.DayNumber%7+1 = 6 then Variance else 0 end as V6,
							case when css.DayNumber%7+1 = 7 then Variance else 0 end as V7,
							case when css.DayNumber%7+1 = 1 then Deposit else 0 end as D1,
							case when css.DayNumber%7+1 = 2 then Deposit else 0 end as D2,
							case when css.DayNumber%7+1 = 3 then Deposit else 0 end as D3,
							case when css.DayNumber%7+1 = 4 then Deposit else 0 end as D4,
							case when css.DayNumber%7+1 = 5 then Deposit else 0 end as D5,
							case when css.DayNumber%7+1 = 6 then Deposit else 0 end as D6,
							case when css.DayNumber%7+1 = 7 then Deposit else 0 end as D7,
							case when css.DayNumber%7+1 = 1 then DepositCash else 0 end as DepositCash1,
							case when css.DayNumber%7+1 = 2 then DepositCash else 0 end as DepositCash2,
							case when css.DayNumber%7+1 = 3 then DepositCash else 0 end as DepositCash3,
							case when css.DayNumber%7+1 = 4 then DepositCash else 0 end as DepositCash4,
							case when css.DayNumber%7+1 = 5 then DepositCash else 0 end as DepositCash5,
							case when css.DayNumber%7+1 = 6 then DepositCash else 0 end as DepositCash6,
							case when css.DayNumber%7+1 = 7 then DepositCash else 0 end as DepositCash7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCard else 0 end as DepositCreditCard1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCard else 0 end as DepositCreditCard2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCard else 0 end as DepositCreditCard3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCard else 0 end as DepositCreditCard4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCard else 0 end as DepositCreditCard5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCard else 0 end as DepositCreditCard6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCard else 0 end as DepositCreditCard7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardVISA else 0 end as DepositCreditCardVISA7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardMC else 0 end as DepositCreditCardMC1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardMC else 0 end as DepositCreditCardMC2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardMC else 0 end as DepositCreditCardMC3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardMC else 0 end as DepositCreditCardMC4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardMC else 0 end as DepositCreditCardMC5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardMC else 0 end as DepositCreditCardMC6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardMC else 0 end as DepositCreditCardMC7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardSwiped else 0 end as DepositCreditCardSwiped7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardDiscover else 0 end as DepositCreditCardDiscover7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardOther else 0 end as DepositCreditCardOther1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardOther else 0 end as DepositCreditCardOther2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardOther else 0 end as DepositCreditCardOther3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardOther else 0 end as DepositCreditCardOther4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardOther else 0 end as DepositCreditCardOther5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardOther else 0 end as DepositCreditCardOther6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardOther else 0 end as DepositCreditCardOther7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardEMV else 0 end as DepositCreditCardEMV7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardOLO else 0 end as DepositCreditCardOLO7,
							case when css.DayNumber%7+1 = 1 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx1,
							case when css.DayNumber%7+1 = 2 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx2,
							case when css.DayNumber%7+1 = 3 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx3,
							case when css.DayNumber%7+1 = 4 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx4,
							case when css.DayNumber%7+1 = 5 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx5,
							case when css.DayNumber%7+1 = 6 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx6,
							case when css.DayNumber%7+1 = 7 then DepositCreditCardAmEx else 0 end as DepositCreditCardAmEx7,
							case when css.DayNumber%7+1 = 1 then ReceiptCount else 0 end as Q1,
							case when css.DayNumber%7+1 = 2 then ReceiptCount else 0 end as Q2,
							case when css.DayNumber%7+1 = 3 then ReceiptCount else 0 end as Q3,
							case when css.DayNumber%7+1 = 4 then ReceiptCount else 0 end as Q4,
							case when css.DayNumber%7+1 = 5 then ReceiptCount else 0 end as Q5,
							case when css.DayNumber%7+1 = 6 then ReceiptCount else 0 end as Q6,
							case when css.DayNumber%7+1 = 7 then ReceiptCount else 0 end as Q7,
							case when css.DayNumber%7+1 = 1 then CheckCount else 0 end as CheckCount1,
							case when css.DayNumber%7+1 = 2 then CheckCount else 0 end as CheckCount2,
							case when css.DayNumber%7+1 = 3 then CheckCount else 0 end as CheckCount3,
							case when css.DayNumber%7+1 = 4 then CheckCount else 0 end as CheckCount4,
							case when css.DayNumber%7+1 = 5 then CheckCount else 0 end as CheckCount5,
							case when css.DayNumber%7+1 = 6 then CheckCount else 0 end as CheckCount6,
							case when css.DayNumber%7+1 = 7 then CheckCount else 0 end as CheckCount7,
							case when css.DayNumber%7+1 = 1 then DeclaredCoverCount else 0 end as DeclaredCoverCount1,
							case when css.DayNumber%7+1 = 2 then DeclaredCoverCount else 0 end as DeclaredCoverCount2,
							case when css.DayNumber%7+1 = 3 then DeclaredCoverCount else 0 end as DeclaredCoverCount3,
							case when css.DayNumber%7+1 = 4 then DeclaredCoverCount else 0 end as DeclaredCoverCount4,
							case when css.DayNumber%7+1 = 5 then DeclaredCoverCount else 0 end as DeclaredCoverCount5,
							case when css.DayNumber%7+1 = 6 then DeclaredCoverCount else 0 end as DeclaredCoverCount6,
							case when css.DayNumber%7+1 = 7 then DeclaredCoverCount else 0 end as DeclaredCoverCount7,
							case when css.DayNumber%7+1 = 1 then ReceiptAmount else 0 end as ReceiptAmount1,
							case when css.DayNumber%7+1 = 2 then ReceiptAmount else 0 end as ReceiptAmount2,
							case when css.DayNumber%7+1 = 3 then ReceiptAmount else 0 end as ReceiptAmount3,
							case when css.DayNumber%7+1 = 4 then ReceiptAmount else 0 end as ReceiptAmount4,
							case when css.DayNumber%7+1 = 5 then ReceiptAmount else 0 end as ReceiptAmount5,
							case when css.DayNumber%7+1 = 6 then ReceiptAmount else 0 end as ReceiptAmount6,
							case when css.DayNumber%7+1 = 7 then ReceiptAmount else 0 end as ReceiptAmount7,
							case when css.DayNumber%7+1 = 1 then TipsPaidAmount else 0 end as TipsPaidAmount1,
							case when css.DayNumber%7+1 = 2 then TipsPaidAmount else 0 end as TipsPaidAmount2,
							case when css.DayNumber%7+1 = 3 then TipsPaidAmount else 0 end as TipsPaidAmount3,
							case when css.DayNumber%7+1 = 4 then TipsPaidAmount else 0 end as TipsPaidAmount4,
							case when css.DayNumber%7+1 = 5 then TipsPaidAmount else 0 end as TipsPaidAmount5,
							case when css.DayNumber%7+1 = 6 then TipsPaidAmount else 0 end as TipsPaidAmount6,
							case when css.DayNumber%7+1 = 7 then TipsPaidAmount else 0 end as TipsPaidAmount7,
							case when css.DayNumber%7+1 = 1 then TipsWithheldAmount else 0 end as TipsWithheldAmount1,
							case when css.DayNumber%7+1 = 2 then TipsWithheldAmount else 0 end as TipsWithheldAmount2,
							case when css.DayNumber%7+1 = 3 then TipsWithheldAmount else 0 end as TipsWithheldAmount3,
							case when css.DayNumber%7+1 = 4 then TipsWithheldAmount else 0 end as TipsWithheldAmount4,
							case when css.DayNumber%7+1 = 5 then TipsWithheldAmount else 0 end as TipsWithheldAmount5,
							case when css.DayNumber%7+1 = 6 then TipsWithheldAmount else 0 end as TipsWithheldAmount6,
							case when css.DayNumber%7+1 = 7 then TipsWithheldAmount else 0 end as TipsWithheldAmount7,
							case when css.DayNumber%7+1 = 1 then GrossReceiptAmount else 0 end as GrossReceiptAmount1,
							case when css.DayNumber%7+1 = 2 then GrossReceiptAmount else 0 end as GrossReceiptAmount2,
							case when css.DayNumber%7+1 = 3 then GrossReceiptAmount else 0 end as GrossReceiptAmount3,
							case when css.DayNumber%7+1 = 4 then GrossReceiptAmount else 0 end as GrossReceiptAmount4,
							case when css.DayNumber%7+1 = 5 then GrossReceiptAmount else 0 end as GrossReceiptAmount5,
							case when css.DayNumber%7+1 = 6 then GrossReceiptAmount else 0 end as GrossReceiptAmount6,
							case when css.DayNumber%7+1 = 7 then GrossReceiptAmount else 0 end as GrossReceiptAmount7,
							case when css.DayNumber%7+1 = 1 then TaxAmount else 0 end as TaxAmount1,
							case when css.DayNumber%7+1 = 2 then TaxAmount else 0 end as TaxAmount2,
							case when css.DayNumber%7+1 = 3 then TaxAmount else 0 end as TaxAmount3,
							case when css.DayNumber%7+1 = 4 then TaxAmount else 0 end as TaxAmount4,
							case when css.DayNumber%7+1 = 5 then TaxAmount else 0 end as TaxAmount5,
							case when css.DayNumber%7+1 = 6 then TaxAmount else 0 end as TaxAmount6,
							case when css.DayNumber%7+1 = 7 then TaxAmount else 0 end as TaxAmount7,
							case when css.DayNumber%7+1 = 1 then ServerCommissionAmount else 0 end as ServerCommissionAmount1,
							case when css.DayNumber%7+1 = 2 then ServerCommissionAmount else 0 end as ServerCommissionAmount2,
							case when css.DayNumber%7+1 = 3 then ServerCommissionAmount else 0 end as ServerCommissionAmount3,
							case when css.DayNumber%7+1 = 4 then ServerCommissionAmount else 0 end as ServerCommissionAmount4,
							case when css.DayNumber%7+1 = 5 then ServerCommissionAmount else 0 end as ServerCommissionAmount5,
							case when css.DayNumber%7+1 = 6 then ServerCommissionAmount else 0 end as ServerCommissionAmount6,
							case when css.DayNumber%7+1 = 7 then ServerCommissionAmount else 0 end as ServerCommissionAmount7,
							case when css.DayNumber%7+1 = 1 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount1,
							case when css.DayNumber%7+1 = 2 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount2,
							case when css.DayNumber%7+1 = 3 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount3,
							case when css.DayNumber%7+1 = 4 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount4,
							case when css.DayNumber%7+1 = 5 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount5,
							case when css.DayNumber%7+1 = 6 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount6,
							case when css.DayNumber%7+1 = 7 then GiftCardSoldAmount else 0 end as GiftCardSoldAmount7,
							case when css.DayNumber%7+1 = 1 then ARPaidInAmount else 0 end as ARPaidInAmount1,
							case when css.DayNumber%7+1 = 2 then ARPaidInAmount else 0 end as ARPaidInAmount2,
							case when css.DayNumber%7+1 = 3 then ARPaidInAmount else 0 end as ARPaidInAmount3,
							case when css.DayNumber%7+1 = 4 then ARPaidInAmount else 0 end as ARPaidInAmount4,
							case when css.DayNumber%7+1 = 5 then ARPaidInAmount else 0 end as ARPaidInAmount5,
							case when css.DayNumber%7+1 = 6 then ARPaidInAmount else 0 end as ARPaidInAmount6,
							case when css.DayNumber%7+1 = 7 then ARPaidInAmount else 0 end as ARPaidInAmount7,
							case when css.DayNumber%7+1 = 1 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount1,
							case when css.DayNumber%7+1 = 2 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount2,
							case when css.DayNumber%7+1 = 3 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount3,
							case when css.DayNumber%7+1 = 4 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount4,
							case when css.DayNumber%7+1 = 5 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount5,
							case when css.DayNumber%7+1 = 6 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount6,
							case when css.DayNumber%7+1 = 7 then GiftCardRedeemedAmount else 0 end as GiftCardRedeemedAmount7,
							case when css.DayNumber%7+1 = 1 then PaidOutAmount else 0 end as PaidOutAmount1,
							case when css.DayNumber%7+1 = 2 then PaidOutAmount else 0 end as PaidOutAmount2,
							case when css.DayNumber%7+1 = 3 then PaidOutAmount else 0 end as PaidOutAmount3,
							case when css.DayNumber%7+1 = 4 then PaidOutAmount else 0 end as PaidOutAmount4,
							case when css.DayNumber%7+1 = 5 then PaidOutAmount else 0 end as PaidOutAmount5,
							case when css.DayNumber%7+1 = 6 then PaidOutAmount else 0 end as PaidOutAmount6,
							case when css.DayNumber%7+1 = 7 then PaidOutAmount else 0 end as PaidOutAmount7,
							case when css.DayNumber%7+1 = 1 then ChargeAmount else 0 end as ChargeAmount1,
							case when css.DayNumber%7+1 = 2 then ChargeAmount else 0 end as ChargeAmount2,
							case when css.DayNumber%7+1 = 3 then ChargeAmount else 0 end as ChargeAmount3,
							case when css.DayNumber%7+1 = 4 then ChargeAmount else 0 end as ChargeAmount4,
							case when css.DayNumber%7+1 = 5 then ChargeAmount else 0 end as ChargeAmount5,
							case when css.DayNumber%7+1 = 6 then ChargeAmount else 0 end as ChargeAmount6,
							case when css.DayNumber%7+1 = 7 then ChargeAmount else 0 end as ChargeAmount7,
							case when css.DayNumber%7+1 = 1 then NetReceiptAmount else 0 end as NetReceiptAmount1,
							case when css.DayNumber%7+1 = 2 then NetReceiptAmount else 0 end as NetReceiptAmount2,
							case when css.DayNumber%7+1 = 3 then NetReceiptAmount else 0 end as NetReceiptAmount3,
							case when css.DayNumber%7+1 = 4 then NetReceiptAmount else 0 end as NetReceiptAmount4,
							case when css.DayNumber%7+1 = 5 then NetReceiptAmount else 0 end as NetReceiptAmount5,
							case when css.DayNumber%7+1 = 6 then NetReceiptAmount else 0 end as NetReceiptAmount6,
							case when css.DayNumber%7+1 = 7 then NetReceiptAmount else 0 end as NetReceiptAmount7,
							case when css.DayNumber%7+1 = 1 then DiscountAmount else 0 end as DiscountAmount1,
							case when css.DayNumber%7+1 = 2 then DiscountAmount else 0 end as DiscountAmount2,
							case when css.DayNumber%7+1 = 3 then DiscountAmount else 0 end as DiscountAmount3,
							case when css.DayNumber%7+1 = 4 then DiscountAmount else 0 end as DiscountAmount4,
							case when css.DayNumber%7+1 = 5 then DiscountAmount else 0 end as DiscountAmount5,
							case when css.DayNumber%7+1 = 6 then DiscountAmount else 0 end as DiscountAmount6,
							case when css.DayNumber%7+1 = 7 then DiscountAmount else 0 end as DiscountAmount7,
							case when css.DayNumber%7+1 = 1 then TipsReceivedAmount else 0 end as TipsReceivedAmount1,
							case when css.DayNumber%7+1 = 2 then TipsReceivedAmount else 0 end as TipsReceivedAmount2,
							case when css.DayNumber%7+1 = 3 then TipsReceivedAmount else 0 end as TipsReceivedAmount3,
							case when css.DayNumber%7+1 = 4 then TipsReceivedAmount else 0 end as TipsReceivedAmount4,
							case when css.DayNumber%7+1 = 5 then TipsReceivedAmount else 0 end as TipsReceivedAmount5,
							case when css.DayNumber%7+1 = 6 then TipsReceivedAmount else 0 end as TipsReceivedAmount6,
							case when css.DayNumber%7+1 = 7 then TipsReceivedAmount else 0 end as TipsReceivedAmount7,
							case when css.DayNumber%7+1 = 1 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount1,
							case when css.DayNumber%7+1 = 2 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount2,
							case when css.DayNumber%7+1 = 3 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount3,
							case when css.DayNumber%7+1 = 4 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount4,
							case when css.DayNumber%7+1 = 5 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount5,
							case when css.DayNumber%7+1 = 6 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount6,
							case when css.DayNumber%7+1 = 7 then TipsDeclaredAmount else 0 end as TipsDeclaredAmount7,
							case when css.DayNumber%7+1 = 1 then PayrollSalesAmount else 0 end as PayrollSalesAmount1,
							case when css.DayNumber%7+1 = 2 then PayrollSalesAmount else 0 end as PayrollSalesAmount2,
							case when css.DayNumber%7+1 = 3 then PayrollSalesAmount else 0 end as PayrollSalesAmount3,
							case when css.DayNumber%7+1 = 4 then PayrollSalesAmount else 0 end as PayrollSalesAmount4,
							case when css.DayNumber%7+1 = 5 then PayrollSalesAmount else 0 end as PayrollSalesAmount5,
							case when css.DayNumber%7+1 = 6 then PayrollSalesAmount else 0 end as PayrollSalesAmount6,
							case when css.DayNumber%7+1 = 7 then PayrollSalesAmount else 0 end as PayrollSalesAmount7,
							case when css.DayNumber%7+1 = 1 then AmountCreditCardSales else 0 end as CreditCardSalesAmount1,
							case when css.DayNumber%7+1 = 2 then AmountCreditCardSales else 0 end as CreditCardSalesAmount2,
							case when css.DayNumber%7+1 = 3 then AmountCreditCardSales else 0 end as CreditCardSalesAmount3,
							case when css.DayNumber%7+1 = 4 then AmountCreditCardSales else 0 end as CreditCardSalesAmount4,
							case when css.DayNumber%7+1 = 5 then AmountCreditCardSales else 0 end as CreditCardSalesAmount5,
							case when css.DayNumber%7+1 = 6 then AmountCreditCardSales else 0 end as CreditCardSalesAmount6,
							case when css.DayNumber%7+1 = 7 then AmountCreditCardSales else 0 end as CreditCardSalesAmount7,
							case when css.DayNumber%7+1 = 1 then AmountCreditCardTip else 0 end as CreditCardTipAmount1,
							case when css.DayNumber%7+1 = 2 then AmountCreditCardTip else 0 end as CreditCardTipAmount2,
							case when css.DayNumber%7+1 = 3 then AmountCreditCardTip else 0 end as CreditCardTipAmount3,
							case when css.DayNumber%7+1 = 4 then AmountCreditCardTip else 0 end as CreditCardTipAmount4,
							case when css.DayNumber%7+1 = 5 then AmountCreditCardTip else 0 end as CreditCardTipAmount5,
							case when css.DayNumber%7+1 = 6 then AmountCreditCardTip else 0 end as CreditCardTipAmount6,
							case when css.DayNumber%7+1 = 7 then AmountCreditCardTip else 0 end as CreditCardTipAmount7,
							case when css.DayNumber%7+1 = 1 then TaxableSales else 0 end as TaxableSalesAmount1,
							case when css.DayNumber%7+1 = 2 then TaxableSales else 0 end as TaxableSalesAmount2,
							case when css.DayNumber%7+1 = 3 then TaxableSales else 0 end as TaxableSalesAmount3,
							case when css.DayNumber%7+1 = 4 then TaxableSales else 0 end as TaxableSalesAmount4,
							case when css.DayNumber%7+1 = 5 then TaxableSales else 0 end as TaxableSalesAmount5,
							case when css.DayNumber%7+1 = 6 then TaxableSales else 0 end as TaxableSalesAmount6,
							case when css.DayNumber%7+1 = 7 then TaxableSales else 0 end as TaxableSalesAmount7,
							case when css.DayNumber%7+1 = 1 then NonTaxableSales else 0 end as NonTaxableSalesAmount1,
							case when css.DayNumber%7+1 = 2 then NonTaxableSales else 0 end as NonTaxableSalesAmount2,
							case when css.DayNumber%7+1 = 3 then NonTaxableSales else 0 end as NonTaxableSalesAmount3,
							case when css.DayNumber%7+1 = 4 then NonTaxableSales else 0 end as NonTaxableSalesAmount4,
							case when css.DayNumber%7+1 = 5 then NonTaxableSales else 0 end as NonTaxableSalesAmount5,
							case when css.DayNumber%7+1 = 6 then NonTaxableSales else 0 end as NonTaxableSalesAmount6,
							case when css.DayNumber%7+1 = 7 then NonTaxableSales else 0 end as NonTaxableSalesAmount7,
							case when css.DayNumber%7+1 = 1 then BadPayments else 0 end as BadPaymentsAmount1,
							case when css.DayNumber%7+1 = 2 then BadPayments else 0 end as BadPaymentsAmount2,
							case when css.DayNumber%7+1 = 3 then BadPayments else 0 end as BadPaymentsAmount3,
							case when css.DayNumber%7+1 = 4 then BadPayments else 0 end as BadPaymentsAmount4,
							case when css.DayNumber%7+1 = 5 then BadPayments else 0 end as BadPaymentsAmount5,
							case when css.DayNumber%7+1 = 6 then BadPayments else 0 end as BadPaymentsAmount6,
							case when css.DayNumber%7+1 = 7 then BadPayments else 0 end as BadPaymentsAmount7,
							css.AmountFood as AmountFood,
							css.AmountBeverage as AmountBeverage,
							css.AmountOther as AmountOther,
							css.AmountDiscount as AmountDiscount,
							css.AmountTax as AmountTax,
							css.AmountCreditCardSales as AmountCreditCardSales,
							css.AmountCreditCardTip as AmountCreditCardTip,
							css.TaxableSales,
							css.NonTaxableSales,
							css.BadPayments
						from ( /* Receipts Totaled Per WeekDay and Receipt Type */ ");
							ReceiptsDetailMiddleSelect ReceiptsDetailMiddleSelect = new ReceiptsDetailMiddleSelect(rv, 7);
							ReceiptsDetailMiddleSelect.ReceiptDetailsCashTipsEarnedID = this.ReceiptDetailsCashTipsEarnedID;
							ReceiptsDetailMiddleSelect.OutputDetailFields = this.OutputDetailFields;
							sb.Append(ReceiptsDetailMiddleSelect.OutputSelect());
							sb.Append(@"
							) css
						left outer join
							tblProfitCenters tblProfitCenters with (nolock)
						on tblProfitCenters.StoreID=css.StoreID
							and tblProfitCenters.ProfitCenterID=css.ProfitCenterID
						left outer join
							tblStores tblStores with (nolock)
						on css.StoreID=tblStores.StoreID
						left outer join
							tblStoreGroups tblStoreGroups with (nolock)
						on tblStoreGroups.StoreGroupID=tblStores.StoreGroupID
						) cs
					where (
						isnull(cs.TotalReceipts,0) <> 0 or isnull(cs.TotalOverShort,0)<>0 or isnull(cs.NetReceipts,0)<>0
							or isnull(cs.ReceiptCount,0)<>0 or cs.DiscountAmount<>0
							)" + OutputWhere(true) + @"
					group by
						cs.PostingDate,
						cs.Preliminary");
					sb.Append(OutputGroupBy(Select.CommaModes.Before, TabCount+1));
					sb.Append(@",
						R1, R2, R3, R4, R5, R6, R7,
						C1, C2, C3, C4, C5, C6, C7,
						V1, V2, V3, V4, V5, V6, V7,
						D1, D2, D3, D4, D5, D6, D7,
						DepositCash1, DepositCash2, DepositCash3, DepositCash4, DepositCash5, DepositCash6, DepositCash7,
						DepositCreditCard1, DepositCreditCard2, DepositCreditCard3, DepositCreditCard4, DepositCreditCard5, DepositCreditCard6, DepositCreditCard7,
						DepositCreditCardVISA1,     DepositCreditCardVISA2,     DepositCreditCardVISA3,     DepositCreditCardVISA4,     DepositCreditCardVISA5,     DepositCreditCardVISA6,     DepositCreditCardVISA7,
						DepositCreditCardMC1,       DepositCreditCardMC2,       DepositCreditCardMC3,       DepositCreditCardMC4,       DepositCreditCardMC5,       DepositCreditCardMC6,       DepositCreditCardMC7,
						DepositCreditCardSwiped1,   DepositCreditCardSwiped2,   DepositCreditCardSwiped3,   DepositCreditCardSwiped4,   DepositCreditCardSwiped5,   DepositCreditCardSwiped6,   DepositCreditCardSwiped7,
						DepositCreditCardDiscover1, DepositCreditCardDiscover2, DepositCreditCardDiscover3, DepositCreditCardDiscover4, DepositCreditCardDiscover5, DepositCreditCardDiscover6, DepositCreditCardDiscover7,
						DepositCreditCardOther1, DepositCreditCardOther2, DepositCreditCardOther3, DepositCreditCardOther4, DepositCreditCardOther5, DepositCreditCardOther6, DepositCreditCardOther7,
						DepositCreditCardEMV1,      DepositCreditCardEMV2,      DepositCreditCardEMV3,      DepositCreditCardEMV4,      DepositCreditCardEMV5,      DepositCreditCardEMV6,      DepositCreditCardEMV7,
						DepositCreditCardOLO1,      DepositCreditCardOLO2,      DepositCreditCardOLO3,      DepositCreditCardOLO4,      DepositCreditCardOLO5,      DepositCreditCardOLO6,      DepositCreditCardOLO7,
						DepositCreditCardAmEx1,     DepositCreditCardAmEx2,     DepositCreditCardAmEx3,     DepositCreditCardAmEx4,     DepositCreditCardAmEx5,     DepositCreditCardAmEx6,     DepositCreditCardAmEx7,
						GiftCardRedeemedAmount1, GiftCardRedeemedAmount2, GiftCardRedeemedAmount3, GiftCardRedeemedAmount4, GiftCardRedeemedAmount5, GiftCardRedeemedAmount6, GiftCardRedeemedAmount7,
						C1-D1-GiftCardRedeemedAmount1, C2-D2-GiftCardRedeemedAmount2, C3-D3-GiftCardRedeemedAmount3, C4-D4-GiftCardRedeemedAmount4, C5-D5-GiftCardRedeemedAmount5, C6-D6-GiftCardRedeemedAmount6, C7-D7-GiftCardRedeemedAmount7,
						NetReceipts-TotalGiftCardRedeemed-TotalDeposit");
				return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
				}
			public ReceiptsOuterSelect(DataAccessReceiptsReview rv, Int32 TabCount) {
				this.rv = rv;
				this.TabCount = TabCount;
				Select.Dimension dd = new Select.Dimension(this);
					Dimensions.Add("StoreGroupID", dd);
					dd.Condition = (rv.StoreGroupFlag!=0);
					Select.Dimension TopStoreGroupDimension = dd;
					Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "StoreGroupID";
						kc.KeyExpression = "StoreGroupID";
					Select.DataColumn dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "StoreGroupName";
						dc.ThenExpression = "StoreGroupName";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "StoreGroupNumber";
						dc.ThenExpression = "StoreGroupNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("StoreID", dd);
					dd.Condition = (rv.StoreFlag!=0 || rv.CreditCardBatchNumberFlag!=0);
					Select.Dimension TopStoreDimension = dd;
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "StoreID";
						kc.KeyExpression = "StoreID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "StoreNumber";
						dc.ThenExpression = "StoreNumber";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "StoreName";
						dc.ThenExpression = "StoreName";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "StoreGCode";
						dc.ThenExpression = "StoreGCode";
				dd = new Select.Dimension(this);
					Dimensions.Add("ProfitCenterID", dd);
					dd.Condition = (rv.ProfitCenterFlag!=0);
					Select.Dimension TopProfitCenterDimension = dd;
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ProfitCenterID";
						kc.KeyExpression = "ProfitCenterID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ProfitCenter";
						dc.ThenExpression = "ProfitCenter";
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptClass", dd);
					dd.Condition = (rv.ReceiptClassFlag!=0);
					Select.Dimension TopReceiptClassDimension = dd;
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptClass";
						kc.KeyExpression = "ReceiptClass";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ReceiptClass";
						dc.ThenExpression = "ReceiptClass";
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptTypeID", dd);
					dd.Condition = (rv.ReceiptTypeFlag!=0);
					Select.Dimension TopReceiptTypeDimension = dd;
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptTypeID";
						kc.KeyExpression = "ReceiptTypeID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ReceiptType";
						dc.ThenExpression = "ReceiptType";
				dd = new Select.Dimension(this);
					Dimensions.Add("CreditCardBatchNumber", dd);
					dd.Condition = (rv.CreditCardBatchNumberFlag!=0);
					Select.Dimension TopCreditCardBatchNumberDimension = dd;
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CreditCardBatchNumber";
						kc.KeyExpression = "CreditCardBatchNumber";
					Select.Dimension.WhereList.Where where = new Select.Dimension.WhereList.Where(dd.Condition);
						dd.WhereObjects.Add(where);
						where.WhereExpression = "CreditCardBatchNumber<>0";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierStaffDepartmentID", dd);
					dd.Condition = (rv.CashierStaffDepartmentFlag!=0);
					Select.Dimension TopCashierStaffDepartmentDimension = dd;
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierStaffDepartmentID";
						kc.KeyExpression = "CashierStaffDepartmentID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CashierStaffDepartmentName";
						dc.ThenExpression = "CashierStaffDepartmentName";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierStaffID", dd);
					dd.Condition = (rv.CashierFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierStaffID";
						kc.KeyExpression = "CashierStaffID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CashierName";
						dc.ThenExpression = "CashierName";
				dd = new Select.Dimension(this);
					Dimensions.Add("ServerStaffDepartmentID", dd);
					dd.Condition = (rv.ServerStaffDepartmentFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ServerStaffDepartmentID";
						kc.KeyExpression = "ServerStaffDepartmentID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ServerStaffDepartmentName";
						dc.ThenExpression = "ServerStaffDepartmentName";
				dd = new Select.Dimension(this);
					Dimensions.Add("ServerStaffID", dd);
					dd.Condition = (rv.ServerFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ServerStaffID";
						kc.KeyExpression = "ServerStaffID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ServerName";
						dc.ThenExpression = "ServerName";
				dd = new Select.Dimension(this);
					Dimensions.Add("CheckNumberID", dd);
					dd.Condition = (rv.CheckNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CheckNumberID";
						kc.KeyExpression = "CheckNumberID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CheckNumber";
						dc.ThenExpression = "CheckNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("DayNameID", dd);
					dd.Condition = (rv.DayNameFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "DayNameID";
						kc.KeyExpression = "DayNameID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "DayName";
						dc.ThenExpression = "substring(cast(dbo.fn_CalcDayNumberDate(DayNameID) as varchar(20)),1,3)+' '+cast(day(dbo.fn_CalcDayNumberDate(DayNameID)) as varchar(10))";
				dd = new Select.Dimension(this);
					Dimensions.Add("WeekNameID", dd);
					dd.Condition = (rv.WeekNameFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "WeekNameID";
						kc.KeyExpression = "WeekNameID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "WeekName";
						dc.ThenExpression = "substring(cast(dbo.fn_CalcDayNumberDate(WeekNameID*7) as varchar(20)),1,3)+' '+cast(day(dbo.fn_CalcDayNumberDate(WeekNameID*7)) as varchar(10))";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierSessionNumber", dd);
					dd.Condition = (rv.CashierSessionNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierSessionNumber";
						kc.KeyExpression = "CashierSessionNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("DayHourNumber", dd);
					dd.Condition = (rv.DayHourNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "DayHourNumber";
						kc.KeyExpression = "DayHourNumber";
				}
			}
		private class ReceiptsDetailMiddleSelect : Select {
			public Int32 ReceiptDetailsCashTipsEarnedID = 0;
			DataAccessReceiptsReview rv;
			public override String OutputSelect(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				sb.Append(@"
					select
						csr.DayNumber,
						case when @CheckNumberFlag=1 then csr.PostingDate else null end as PostingDate" + 
						OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
						case when @CashierFlag=0 and @ServerFlag=0 and @CheckNumberFlag=0 then csr.Expected else null end as Expected,
						case when @CashierFlag=0 and @ServerFlag=0 and @CheckNumberFlag=0 then csr.Counted end as Counted,
						case when @CashierFlag=0 and @ServerFlag=0 and @CheckNumberFlag=0 then csr.Variance else null end as Variance,
						csr.Preliminary,");
				if (OutputDetailFields)
					sb.Append(@"
						sum(csrd.Deposit) as Deposit,
						sum(csrd.DepositCash) as DepositCash,
						sum(csrd.DepositCreditCard) as DepositCreditCard,
						sum(csrd.DepositCreditCardVISA) as DepositCreditCardVISA,
						sum(csrd.DepositCreditCardMC) as DepositCreditCardMC,
						sum(csrd.DepositCreditCardSwiped) as DepositCreditCardSwiped,
						sum(csrd.DepositCreditCardDiscover) as DepositCreditCardDiscover,
						sum(csrd.DepositCreditCardOther) as DepositCreditCardOther,
						sum(csrd.DepositCreditCardEMV) as DepositCreditCardEMV,
						sum(csrd.DepositCreditCardOLO) as DepositCreditCardOLO,
						sum(csrd.DepositCreditCardAmEx) as DepositCreditCardAmEx,");
				else
					sb.Append(@"
						csr.Deposit,
						csr.DepositCash,
						csr.DepositCreditCard,
						csr.DepositCreditCardVISA,
						csr.DepositCreditCardMC,
						csr.DepositCreditCardSwiped,
						csr.DepositCreditCardDiscover,
						csr.DepositCreditCardOther,
						csr.DepositCreditCardEMV,
						csr.DepositCreditCardOLO,
						csr.DepositCreditCardAmEx,");
				sb.Append(@"
						sum(csrd.ReceiptCount) as ReceiptCount,
						sum(csrd.CheckCount) as CheckCount,
						sum(csrd.DeclaredCoverCount) as DeclaredCoverCount,
						sum(csrd.ReceiptAmount) as ReceiptAmount,
						sum(csrd.TipsPaidAmount) as TipsPaidAmount,
						sum(csrd.TipsWithheldAmount) as TipsWithheldAmount,
						sum(csrd.GrossReceiptAmount) as GrossReceiptAmount,
						sum(csrd.TaxAmount) as TaxAmount,
						sum(csrd.ServerCommission) as ServerCommissionAmount,
						sum(csrd.GiftCardSold) as GiftCardSoldAmount,
						sum(csrd.ARPaidIn) as ARPaidInAmount,
						sum(csrd.GiftCardRedeemed) as GiftCardRedeemedAmount,
						sum(csrd.PaidOutAmount) as PaidOutAmount,
						sum(csrd.AmountCharge) as ChargeAmount,
						sum(csrd.NetReceiptAmount) as NetReceiptAmount,
						sum(csrd.DiscountAmount) as DiscountAmount,
						sum(csrd.TipsReceivedAmount) as TipsReceivedAmount,
						sum(csrd.TipsDeclaredAmount) as TipsDeclaredAmount,
						sum(csrd.PayrollSalesAmount) as PayrollSalesAmount,
						sum(csrd.AmountFood) as AmountFood,
						sum(csrd.AmountBeverage) as AmountBeverage,
						sum(csrd.AmountOther) as AmountOther,
						sum(csrd.AmountDiscount) as AmountDiscount,
						sum(csrd.AmountTax) as AmountTax,
						sum(csrd.AmountCreditCardSales) as AmountCreditCardSales,
						sum(csrd.AmountCreditCardTip) as AmountCreditCardTip,
						sum(csrd.TaxableSales) as TaxableSales,
						sum(csrd.NonTaxableSales) as NonTaxableSales,
						sum(csrd.BadPayments) as BadPayments
					from ( /* merges columns sourced on Daily Totals and columns sourced on Totals of Daily Receipts Detail */ ");
						ReceiptsDetailUpperSelect0 ReceiptsDetailUpperSelect0 = new ReceiptsDetailUpperSelect0(rv, TabCount+1);
						sb.Append(ReceiptsDetailUpperSelect0.OutputSelect());
						sb.Append(@"
						) csr
					inner join (");
						ReceiptsDetailSelectSum ReceiptsDetailSelectSum = new ReceiptsDetailSelectSum(rv, TabCount+1);
						ReceiptsDetailSelectSum.ReceiptDetailsCashTipsEarnedID = this.ReceiptDetailsCashTipsEarnedID;
						sb.Append(ReceiptsDetailSelectSum.OutputSelect());
						sb.Append(@"
						) csrd
					on csrd.StoreID=csr.StoreID
						and csrd.DayNumber=csr.DayNumber" + 
						OutputJoinExpressions(Select.AndModes.Before) + 
					OutputJoin() + @"
					group by
						csr.DayNumber,
						case when @CheckNumberFlag=1 then csr.PostingDate else null end" +
						OutputGroupBy(Select.CommaModes.BeforeAndAfter) + @"
						case when @CashierFlag=0 and @ServerFlag=0 and @CheckNumberFlag=0 then csr.Expected else null end,
						case when @CashierFlag=0 and @ServerFlag=0 and @CheckNumberFlag=0 then csr.Counted else null end,
						case when @CashierFlag=0 and @ServerFlag=0 and @CheckNumberFlag=0 then csr.Variance else null end,
						csr.Preliminary,
						csr.Deposit,
						csr.DepositCash,
						csr.DepositCreditCard,
						csr.DepositCreditCardVISA,
						csr.DepositCreditCardMC,
						csr.DepositCreditCardSwiped,
						csr.DepositCreditCardDiscover,
						csr.DepositCreditCardOther,
						csr.DepositCreditCardEMV,
						csr.DepositCreditCardOLO,
						csr.DepositCreditCardAmEx");
				return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
				}
			public ReceiptsDetailMiddleSelect(DataAccessReceiptsReview rv, Int32 TabCount) {
				this.rv = rv;
				this.TabCount = TabCount;
				Select.Dimension dd = new Select.Dimension(this);
					Dimensions.Add("StoreID", dd);
					dd.Condition = (rv.StoreFlag!=0 || rv.CreditCardBatchNumberFlag!=0 || rv.ProfitCenterFlag!=0);
					Select.Dimension StoreDimension = dd;
					Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "StoreID";
						kc.KeyExpression = "csr.StoreID";
				dd = new Select.Dimension(this);
					Dimensions.Add("ProfitCenterID", dd);
					dd.Condition = (rv.ProfitCenterFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ProfitCenterID";
						kc.KeyExpression = "csrd.ProfitCenterID";
					Select.DataColumn dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ProfitCenter";
						dc.ThenExpression = "csrd.ProfitCenter";
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptClassID", dd);
					dd.Condition = (rv.ReceiptClassFlag!=0);
					dd.JoinExpression = "csrd.ReceiptClassID=csr.ReceiptClassID";
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptClassID";
						kc.KeyExpression = "csr.ReceiptClassID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ReceiptClass";
						dc.ThenExpression = "csr.ReceiptClass";		
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptTypeID", dd);
					dd.Condition = (rv.ReceiptTypeFlag!=0);
					dd.JoinExpression = "csrd.ReceiptTypeID=csr.ReceiptTypeID";
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptTypeID";
						kc.KeyExpression = "csr.ReceiptTypeID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ReceiptType";
						dc.ThenExpression = "csr.ReceiptType";		
				dd = new Select.Dimension(this);
					Dimensions.Add("CreditCardBatchNumber", dd);
					dd.Condition = (rv.CreditCardBatchNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CreditCardBatchNumber";
						kc.KeyExpression = "CreditCardBatchNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierStaffDepartmentID", dd);
					dd.Condition = (rv.CashierStaffDepartmentFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierStaffDepartmentID";
						kc.KeyExpression = "csrd.CashierStaffDepartmentID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CashierStaffDepartmentName";
						dc.ThenExpression = "sdCashier.StaffDepartmentName";
						Select.DataColumn.Join jn = new Select.DataColumn.Join();
							dc.Joins.Add(jn);
							jn.TableExpression = "@tblStaffCashierStaffDepartmentNames sdCashier";
							jn.JoinExpression = "csrd.StoreID*4294967296+csrd.CashierStaffDepartmentID=sdCashier.TableKey";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierStaffID", dd);
					dd.Condition = (rv.CashierFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierStaffID";
						kc.KeyExpression = "csrd.CashierStaffID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CashierName";
						dc.ThenExpression = "isnull(rtrim(stfc.LastName) + ', ','') + isnull(stfc.FirstName,'')";
						jn = new Select.DataColumn.Join();
							dc.Joins.Add(jn);
							jn.TableExpression = "tblStaff stfc";
							jn.JoinExpression = "stfc.StoreID=csrd.StoreID and stfc.StoreStaffID=csrd.CashierStaffID";
				dd = new Select.Dimension(this);
					Dimensions.Add("ServerStaffDepartmentID", dd);
					dd.Condition = (rv.ServerStaffDepartmentFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ServerStaffDepartmentID";
						kc.KeyExpression = "csrd.ServerStaffDepartmentID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ServerStaffDepartmentName";
						dc.ThenExpression = "sdServer.StaffDepartmentName";
						jn = new Select.DataColumn.Join();
							dc.Joins.Add(jn);
							jn.TableExpression = "@tblStaffServerStaffDepartmentNames sdServer";
							jn.JoinExpression = "csrd.StoreID*4294967296+csrd.ServerStaffDepartmentID=sdServer.TableKey";
				dd = new Select.Dimension(this);
					Dimensions.Add("ServerStaffID", dd);
					dd.Condition = (rv.ServerFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ServerStaffID";
						kc.KeyExpression = "csrd.ServerStaffID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ServerName";
						dc.ThenExpression = "isnull(rtrim(stfs.LastName) + ', ','') + isnull(stfs.FirstName,'')";
						jn = new Select.DataColumn.Join();
							dc.Joins.Add(jn);
							jn.TableExpression = "tblStaff stfs";
							jn.JoinExpression = "stfs.StoreID=csrd.StoreID and stfs.StoreStaffID=csrd.ServerStaffID";
				dd = new Select.Dimension(this);
					Dimensions.Add("CheckNumberID", dd);
					dd.Condition = (rv.CheckNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CheckNumberID";
						kc.KeyExpression = "csrd.CheckNumberID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CheckNumber";
						dc.ThenExpression = "cast(floor(csrd.CheckNumber/10000000000) as varchar(10))+'-'+cast(csrd.CheckNumber as varchar(10))+'-'+cast(csrd.PersonNumber as varchar(10))";
				dd = new Select.Dimension(this);
					Dimensions.Add("DayNameID", dd);
					dd.Condition = (rv.DayNameFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "DayNameID";
						kc.KeyExpression = "csrd.DayNumber";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "DayName";
						dc.ThenExpression = "substring(cast(dbo.fn_CalcDayNumberDate(csrd.DayNumber) as varchar(20)),1,3)+' '+cast(day(dbo.fn_CalcDayNumberDate(csrd.DayNumber)) as varchar(10))";
				dd = new Select.Dimension(this);
					Dimensions.Add("WeekNameID", dd);
					dd.Condition = (rv.WeekNameFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "WeekNameID";
						kc.KeyExpression = "floor(csrd.DayNumber/7)";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "WeekName";
						dc.ThenExpression = "substring(cast(dbo.fn_CalcDayNumberDate(floor(csrd.DayNumber/7)*7) as varchar(20)),1,3)+' '+cast(day(dbo.fn_CalcDayNumberDate(floor(csrd.DayNumber/7)*7)) as varchar(10))";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierSessionNumber", dd);
					dd.Condition = (rv.CashierSessionNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierSessionNumber";
						kc.KeyExpression = "csrd.CashierSessionNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("DayHourNumber", dd);
					dd.Condition = (rv.DayHourNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "DayHourNumber";
						kc.KeyExpression = "DayHourNumber";
				}
			}
		private class ReceiptsDetailUpperSelect0 : Select {
			DataAccessReceiptsReview rv;
			public override String OutputSelect(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				sb.Append(@"
					select
						bmq.DayNumber,
						bmq.PostingDate,
						bmq.StoreID,
						max(case when Preliminary<>0 then 1 else 0 end) as Preliminary" +
						OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
						sum(Expected) as Expected,
						sum(Counted) as Counted,
						sum(Variance) as Variance,
						sum(case when bmq.ReceiptClassID in (1,2) then Counted else 0 end) as Deposit,
						sum(case when bmq.ReceiptClassID=1 then Counted else 0 end) as DepositCash,
						sum(case when bmq.ReceiptClassID=2 then Counted else 0 end) as DepositCreditCard,
						sum(case when rt.ReceiptTypeVISA=0 and bmq.ReceiptTypeID in (5) or rt.ReceiptTypeVISA=1 then Counted else 0 end) as DepositCreditCardVISA,
						sum(case when rt.ReceiptTypeMC=0 and bmq.ReceiptTypeID in (4) or rt.ReceiptTypeMC=1 then Counted else 0 end) as DepositCreditCardMC,
						sum(case when rt.ReceiptTypeEMV in (-1,0) and bmq.ReceiptClassID=2 then Counted else 0 end) as DepositCreditCardSwiped,
						sum(case when rt.ReceiptTypeDiscover=0 and bmq.ReceiptTypeID in (7) or rt.ReceiptTypeDiscover=1 then Counted else 0 end) as DepositCreditCardDiscover,
						sum(case when (rt.ReceiptTypeOther=0 and rt.CreditCardReceipt<>0 and not bmq.ReceiptTypeID in (7,4,5,3)) or rt.ReceiptTypeOther=1 then Counted else 0 end) as DepositCreditCardOther,
						sum(case when rt.ReceiptTypeEMV=1 and bmq.ReceiptClassID=2 then Counted else 0 end) as DepositCreditCardEMV,
						sum(case when rt.ReceiptTypeEMV=2 and bmq.ReceiptClassID=2 then Counted else 0 end) as DepositCreditCardOLO,
						sum(case when rt.ReceiptTypeAmEx=0 and bmq.ReceiptTypeID in (3) or rt.ReceiptTypeAmEx=1 then Counted else 0 end) as DepositCreditCardAmEx
					from ( /* Receipts Daily Totals */ ");
						ReceiptsDetailUpperSelect1 ReceiptsDetailUpperSelect1 = new ReceiptsDetailUpperSelect1(rv, TabCount+1);
						sb.Append(ReceiptsDetailUpperSelect1.OutputSelect());
						sb.Append(@"
						union all /* Discount Receipts From Receipts Detail and Totaled Daily */ ");
						ReceiptsDetailUpperSelect2 ReceiptsDetailUpperSelect2 = new ReceiptsDetailUpperSelect2(rv, TabCount+1);
						sb.Append(ReceiptsDetailUpperSelect2.OutputSelect());
						sb.Append(@"
						)	bmq
					inner join tblReceiptTypes rt with (nolock) on rt.StoreID=bmq.StoreID and rt.ReceiptTypeID=bmq.ReceiptTypeID
					group by
						bmq.DayNumber,
						bmq.PostingDate,
						bmq.StoreID" +
						OutputGroupBy(Select.CommaModes.Before, 5));
				return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
				}
			public ReceiptsDetailUpperSelect0(DataAccessReceiptsReview rv, Int32 TabCount) {
				this.rv = rv;
				this.TabCount = TabCount;
				Select.Dimension dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptClassID", dd);
					dd.Condition = (rv.ReceiptClassFlag!=0);
					Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptClassID";
						kc.KeyExpression = "bmq.ReceiptClassID";
					Select.DataColumn dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ReceiptClass";
						dc.ThenExpression = "bmq.ReceiptClass";
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptTypeID", dd);
					dd.Condition = (rv.ReceiptTypeFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptTypeID";
						kc.KeyExpression = "bmq.ReceiptTypeID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "ReceiptType";
						dc.ThenExpression = "bmq.ReceiptType";
				}
			private class ReceiptsDetailUpperSelect1 : Select {
				/* this select sums up receipt totals */
				DataAccessReceiptsReview rv;
				public override String OutputSelect(Int32 TabCount) {
					StringBuilder sb = new StringBuilder();
					sb.Append(@"
						select
							csbx.DayNumber,
							csbx.PostingDate,
							csbx.StoreID,
							csbx.Preliminary" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							sum(csrx.Expected) as Expected,
							sum(csrx.Counted) as Counted,
							sum(csrx.Variance) as Variance
						from
							tblCashAndSalesReceipts csrx with (nolock)
						inner join
							tblCashAndSalesBatches csbx with (nolock)
						on csrx.BatchID=csbx.BatchID
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csbx.StoreID
							and rt.ReceiptTypeID=csrx.ReceiptTypeID
						inner join
							@tblReceiptClasses rc
						on rc.TableKey=rt.ReceiptClass
						left outer join 
							@RedirectedReceiptsTable brk
						on brk.BatchID=csrx.BatchID
							and brk.RedirectedReceiptTypeID=csrx.ReceiptTypeID
						left outer join 
							@RedirectedReceiptsTable rrt
						on rrt.BatchID=csrx.BatchID
							and rrt.ExcludedReceiptTypeID=csrx.ReceiptTypeID
						where csbx.DayNumber between @StartDayNumber and @EndDayNumber
							and csbx.StoreID in (select StoreID from @tblStores)
							and rt.ReceiptPostedAsDiscount=0
							and csrx.ReceiptTypeID<100");
							if (rv.ReceiptTypeListCSV.Length>0)
								sb.Append (TabString(TabCount+1)+"and (" + ParsePairSetOutput(rv.ReceiptTypeListCSV,"csbx.StoreID","csrx.ReceiptTypeID") + ")");
							sb.Append(@"
						group by
							csbx.BatchID,
							csbx.DayNumber,
							csbx.PostingDate,
							csbx.StoreID,
							csbx.Preliminary" +
							OutputGroupBy(Select.CommaModes.Before, 6));
					return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
					}
				public ReceiptsDetailUpperSelect1(DataAccessReceiptsReview rv, Int32 TabCount) {
					this.rv = rv;
					this.TabCount = TabCount;
					Select.Dimension dd = new Select.Dimension(this);
						Dimensions.Add("ReceiptClassID", dd);
						dd.Condition = true;
						Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
							dd.KeyColumn = kc;
							kc.ColumnName = "ReceiptClassID";
							kc.KeyExpression = "case when isnull(rrt.RedirectedReceiptClassID,rt.ReceiptClass) in (1,2) then isnull(rrt.RedirectedReceiptClassID,rt.ReceiptClass) else 3 end";
						Select.DataColumn dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "ReceiptClass";
							dc.ThenExpression = "case isnull(rrt.RedirectedReceiptClassID,rt.ReceiptClass) when 1 then 'Cash' when 2 then 'Credit Card' else 'Other' end";
					dd = new Select.Dimension(this);
						Dimensions.Add("ReceiptTypeID", dd);
						dd.Condition = true; 
						kc = new Select.KeyDataColumn(dd);
							dd.KeyColumn = kc;
							kc.ColumnName = "ReceiptTypeID";
							kc.KeyExpression = "csrx.ReceiptTypeID";
						dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "ReceiptType";
							dc.ThenExpression = "rt.ReceiptType";
					}
				}
			private class ReceiptsDetailUpperSelect2 : Select {
				/* this select sums up receipts detail to a one receipt per receipt type per day for joining to receipts */
				DataAccessReceiptsReview rv;
				public override String OutputSelect(Int32 TabCount) {
					StringBuilder sb = new StringBuilder();
					sb.Append(@"
						select distinct
							csrd.DayNumber,
							csbx.PostingDate,
							csrd.StoreID,
							0 as Preliminary" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							0 as Expected,
							0 as Counted,
							0 as Variance
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID
						inner join
							tblCashAndSalesBatches csbx with (nolock)
						on csbx.BatchID=csrd.BatchID
						inner join
							@tblReceiptClasses rc
						on rc.TableKey=rt.ReceiptClass
						where csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and csrd.StoreID in (select StoreID from @tblStores)
							and csbx.DayNumber between @StartDayNumber and @EndDayNumber
							and csbx.StoreID in (Select StoreID from @tblStores)
							and rt.ReceiptPostedAsDiscount=1
							and rt.ReceiptTypeID<100");
					if (rv.ReceiptTypeListCSV.Length>0)
						sb.Append (TabString(TabCount)+"and (" + ParsePairSetOutput(rv.ReceiptTypeListCSV,"csbx.StoreID","csrd.ReceiptTypeID")+")");
					return sb.ToString().Replace("\n\t\t\t\t\t", TabString(TabCount));
					}
				public ReceiptsDetailUpperSelect2(DataAccessReceiptsReview rv, Int32 TabCount) {
					this.rv = rv;
					this.TabCount = TabCount;
					Select.Dimension dd = new Select.Dimension(this);
						Dimensions.Add("ReceiptClassID", dd);
						dd.Condition = true;
						Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
							dd.KeyColumn = kc;
							kc.ColumnName = "ReceiptClassID";
							kc.KeyExpression = "case when rt.ReceiptClass in (1,2,1000) then rt.ReceiptClass else 3 end";
						Select.DataColumn dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "ReceiptClass";
							dc.ThenExpression = "case rt.ReceiptClass when 1 then 'Cash' when 2 then 'Credit Card' else 'Other' end";
					dd = new Select.Dimension(this);
						Dimensions.Add("ReceiptTypeID", dd);
						dd.Condition = (rv.ReceiptTypeFlag!=0);
						kc = new Select.KeyDataColumn(dd);
							dd.KeyColumn = kc;
							kc.ColumnName = "ReceiptTypeID";
							kc.KeyExpression = "-csrd.ReceiptTypeID";
						dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "ReceiptType";
							dc.ThenExpression = "rt.ReceiptType";
					}
				}
			}
		private class ReceiptsDetailSelectSum : Select {
			/* this select sums up receipts detail to a one receipt per receipt type per day for joining to receipts */
			DataAccessReceiptsReview rv;
			public Int32 ReceiptDetailsCashTipsEarnedID = 0;
			public override String OutputSelect(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				sb.Append(@"
					select
						iqr1.StoreID,
						iqr1.ReceiptClassID,
						iqr1.ReceiptTypeID,
						iqr1.CreditCardBatchNumber,
						iqr1.DayNumber,
						iqr1.CashierStaffDepartmentID,
						iqr1.CashierStaffID,
						iqr1.ServerStaffDepartmentID,
						iqr1.ServerStaffID,
						iqr1.CheckNumberID,
						iqr1.CheckNumber,
						iqr1.PersonNumber,
						iqr1.ProfitCenterID,
						pft.ProfitCenter,
						iqr1.CashierSessionNumber,
						iqr1.DayHourNumber,
						iqr1.ReceiptCount,
						iqr1.CheckCount,
						iqr1.DeclaredCoverCount,
						iqr1.CashReceiptDivisor,
						iqr1.ReceiptAmount,
						iqr1.TipsPaidAmount,
						iqr1.TipsWithheldAmount,
						iqr1.PaidOutAmount,
						iqr1.ReceiptAmount+iqr1.TipsPaidAmount+iqr1.PaidOutAmount as GrossReceiptAmount,
						iqr1.TaxAmount,
						iqr1.ServerCommission,
						iqr1.GiftCardSold,
						iqr1.ARPaidIn,
						iqr1.GiftCardRedeemed,
						-iqr1.AmountCharge as AmountCharge,
						iqr1.ReceiptAmount+iqr1.TipsPaidAmount+iqr1.TipsWithheldAmount+iqr1.TaxAmount+iqr1.ServerCommission+iqr1.GiftCardSold-iqr1.AmountCharge as NetReceiptAmount,
						iqr1.DiscountAmount,
						iqr1.TipsReceivedAmount,
						iqr1.TipsDeclaredAmount,
						case when pft.IncludeTipsAndSalesOnPayrollReports is null or pft.IncludeTipsAndSalesOnPayrollReports=1
							then iqr1.ReceiptAmount+iqr1.TipsPaidAmount+iqr1.TaxAmount+iqr1.ServerCommission+iqr1.GiftCardSold-iqr1.NonTipAmount
							else 0
						end as PayrollSalesAmount,
						iqr1.AmountFood as AmountFood,
						iqr1.AmountBeverage as AmountBeverage,
						iqr1.AmountOther as AmountOther,
						iqr1.AmountDiscount as AmountDiscount,
						iqr1.AmountTax as AmountTax,
						iqr1.AmountCreditCardSales as AmountCreditCardSales,
						iqr1.AmountCreditCardTip as AmountCreditCardTip,
						iqr1.Deposit,
						iqr1.DepositCash,
						iqr1.DepositCreditCard,
						iqr1.DepositCreditCardVISA,
						iqr1.DepositCreditCardMC,
						iqr1.DepositCreditCardSwiped,
						iqr1.DepositCreditCardDiscover,
						iqr1.DepositCreditCardOther,
						iqr1.DepositCreditCardEMV,
						iqr1.DepositCreditCardOLO,
						iqr1.DepositCreditCardAmEx,
						iqr1.TaxableSales,
						iqr1.NonTaxableSales,
						iqr1.BadPayments
					from (
						select
							hsq.StoreID,
							hsq.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							sum(hsq.ReceiptCount) as ReceiptCount,
							sum(hsq.CheckCount) as CheckCount,
							sum(hsq.DeclaredCoverCount) as DeclaredCoverCount,
							sum(hsq.CashReceiptDivisor) as CashReceiptDivisor,
							sum(hsq.ReceiptAmount) as ReceiptAmount,
							sum(hsq.TipsPaidAmount) as TipsPaidAmount,
							sum(hsq.TipsWithheldAmount) as TipsWithheldAmount,
							sum(hsq.PaidOutAmount) as PaidOutAmount,
							sum(hsq.TaxAmount) as TaxAmount,
							sum(hsq.DiscountAmount) as DiscountAmount,
							sum(hsq.ServerCommission) as ServerCommission,
							sum(hsq.GiftCardSold) as GiftCardSold,
							sum(hsq.ARPaidIn) as ARPaidIn,
							sum(hsq.GiftCardRedeemed) as GiftCardRedeemed,
							sum(hsq.PaidOut) as PaidOut,
							sum(hsq.TipsReceivedAmount) as TipsReceivedAmount,
							sum(hsq.TipsDeclaredAmount) as TipsDeclaredAmount,
							sum(hsq.NonTipAmount) as NonTipAmount,
							sum(hsq.AmountFood) as AmountFood,
							sum(hsq.AmountBeverage) as AmountBeverage,
							sum(hsq.AmountOther) as AmountOther,
							sum(hsq.AmountCharge) as AmountCharge,
							sum(hsq.AmountDiscount) as AmountDiscount,
							sum(hsq.AmountTax) as AmountTax,
							sum(hsq.AmountCreditCardSales) as AmountCreditCardSales,
							sum(hsq.AmountCreditCardTip) as AmountCreditCardTip,
							sum(case when hsq.ReceiptClassID in (1,2) then ReceiptAmount else 0 end) as Deposit,
							sum(case when hsq.ReceiptClassID=1 then ReceiptAmount else 0 end) as DepositCash,
							sum(case when hsq.ReceiptClassID=2 then ReceiptAmount else 0 end) as DepositCreditCard,
							sum(case when rt.ReceiptTypeVISA=0 and hsq.ReceiptTypeID in (5) or rt.ReceiptTypeVISA=1 then ReceiptAmount else 0 end) as DepositCreditCardVISA,
							sum(case when rt.ReceiptTypeMC=0 and hsq.ReceiptTypeID in (4) or rt.ReceiptTypeMC=1 then ReceiptAmount else 0 end) as DepositCreditCardMC,
							sum(case when rt.ReceiptTypeEMV in (-1,0) and hsq.ReceiptClassID=2 then ReceiptAmount else 0 end) as DepositCreditCardSwiped,
							sum(case when rt.ReceiptTypeDiscover=0 and hsq.ReceiptTypeID in (7) or rt.ReceiptTypeDiscover=1 then ReceiptAmount else 0 end) as DepositCreditCardDiscover,
							sum(case when (rt.ReceiptTypeOther=0 and rt.CreditCardReceipt<>0 and not hsq.ReceiptTypeID in (7,4,5,3)) or rt.ReceiptTypeOther=1 then ReceiptAmount else 0 end) as DepositCreditCardOther,
							sum(case when rt.ReceiptTypeEMV=1 and hsq.ReceiptClassID=2 then ReceiptAmount else 0 end) as DepositCreditCardEMV,
							sum(case when rt.ReceiptTypeEMV=2 and hsq.ReceiptClassID=2 then ReceiptAmount else 0 end) as DepositCreditCardOLO,
							sum(case when rt.ReceiptTypeAmEx=0 and hsq.ReceiptTypeID in (3) or rt.ReceiptTypeAmEx=1 then ReceiptAmount else 0 end) as DepositCreditCardAmEx,
							");
				Boolean IsNew = (ReceiptDetailsCashTipsEarnedID!=0); 
				if (IsNew)
					sb.Append(@"
							sum(case when rt.ReceiptPostedAsDiscount=0 then AmountFood+AmountBeverage+AmountOther+AmountDiscount-AmountNonTaxableSale else 0 end) as TaxableSales,
							sum(case when rt.ReceiptPostedAsDiscount=0 then AmountNonTaxableSale else 0 end) as NonTaxableSales,
							");
				else
					sb.Append(@"
							sum(case when rt.ReceiptPostedAsDiscount=0 and AmountTax=0 then 0 else AmountFood+AmountBeverage+AmountOther+AmountDiscount end) as TaxableSales,
							sum(case when rt.ReceiptPostedAsDiscount=0 and AmountTax=0 then AmountFood+AmountBeverage+AmountOther+AmountDiscount else 0 end) as NonTaxableSales,
							");
				sb.Append(@"
							sum(case when rt.ReceiptTypeID=1000 then ReceiptAmount else 0 end) as BadPayments
						from (");
							ReceiptsDetailSelect ReceiptsDetailSelect = new ReceiptsDetailSelect(rv, TabCount+1);
							ReceiptsDetailSelect.ReceiptDetailsCashTipsEarnedID = this.ReceiptDetailsCashTipsEarnedID;
							sb.Append(ReceiptsDetailSelect.OutputSelect());
							sb.Append(@"
							) hsq
						inner join
							tblReceiptTypes rt with (nolock) on rt.StoreID=hsq.StoreID and rt.ReceiptTypeID=hsq.ReceiptTypeID
						group by
							hsq.StoreID,
							hsq.DayNumber" +
							OutputGroupBy(Select.CommaModes.Before) + @"
						) iqr1
					left outer join
						tblProfitCenters pft with (nolock)
					on pft.StoreID=iqr1.StoreID
						and pft.ProfitCenterID=iqr1.ProfitCenterID");
				return sb.ToString().Replace("\n\t\t\t\t\t",TabString(TabCount));
				}
			public ReceiptsDetailSelectSum(DataAccessReceiptsReview rv, Int32 TabCount) {
				this.rv = rv;
				this.TabCount = TabCount;
				Select.Dimension dd = new Select.Dimension(this);
					Dimensions.Add("ProfitCenterID", dd);
					dd.Condition = (rv.ProfitCenterFlag!=0);
					Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ProfitCenterID";
						kc.KeyExpression = "hsq.ProfitCenterID";
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptClassID", dd);
					dd.Condition = (rv.ReceiptClassFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptClassID";
						kc.KeyExpression = "hsq.ReceiptClassID";
				dd = new Select.Dimension(this);
					Dimensions.Add("ReceiptTypeID", dd);
					dd.Condition = (rv.ReceiptTypeFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ReceiptTypeID";
						kc.KeyExpression = "hsq.ReceiptTypeID";
				dd = new Select.Dimension(this);
					Dimensions.Add("CreditCardBatchNumber", dd);
					dd.Condition = (rv.CreditCardBatchNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CreditCardBatchNumber";
						kc.KeyExpression = "hsq.CreditCardBatchNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierStaffDepartmentID", dd);
					dd.Condition = (rv.CashierStaffDepartmentFlag!=0 || rv.CashierStaffDepartmentListCSV.Length>0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierStaffDepartmentID";
						kc.KeyExpression = "hsq.CashierStaffDepartmentID";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierStaffID", dd);
					dd.Condition = (rv.CashierFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierStaffID";
						kc.KeyExpression = "hsq.CashierStaffID";
				dd = new Select.Dimension(this);
					Dimensions.Add("ServerStaffDepartmentID", dd);
					dd.Condition = (rv.ServerStaffDepartmentFlag!=0 || rv.ServerStaffDepartmentListCSV.Length>0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ServerStaffDepartmentID";
						kc.KeyExpression = "hsq.ServerStaffDepartmentID";
				dd = new Select.Dimension(this);
					Dimensions.Add("ServerStaffID", dd);
					dd.Condition = (rv.ServerFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "ServerStaffID";
						kc.KeyExpression = "hsq.ServerStaffID";
				dd = new Select.Dimension(this);
					Dimensions.Add("CheckNumberID", dd);
					dd.Condition = (rv.CheckNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CheckNumberID";
						kc.KeyExpression = "hsq.CheckNumberID";
					Select.DataColumn dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "CheckNumber";
						dc.ThenExpression = "hsq.CheckNumber";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "PersonNumber";
						dc.ThenExpression = "hsq.PersonNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("DayNameID", dd);
					dd.Condition = (rv.DayNameFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "DayNameID";
						kc.KeyExpression = "hsq.DayNameID";
					dc = new Select.DataColumn(dd);
						dd.DataColumns.Add(dc);
						dc.ColumnName = "DayName";
						dc.ThenExpression = "hsq.DayName";
				dd = new Select.Dimension(this);
					Dimensions.Add("CashierSessionNumber", dd);
					dd.Condition = (rv.CashierSessionNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "CashierSessionNumber";
						kc.KeyExpression = "CashierSessionNumber";
				dd = new Select.Dimension(this);
					Dimensions.Add("DayHourNumber", dd);
					dd.Condition = (rv.DayHourNumberFlag!=0);
					kc = new Select.KeyDataColumn(dd);
						dd.KeyColumn = kc;
						kc.ColumnName = "DayHourNumber";
						kc.KeyExpression = "DayHourNumber";
				}
			private class ReceiptsDetailSelect : Select {
				public Int32 ReceiptDetailsCashTipsEarnedID = 0;
				DataAccessReceiptsReview rv;
				public override String OutputSelect(Int32 TabCount) {
					StringBuilder sb = new StringBuilder();
					sb.Append(@"
						select /* class ReceiptsDetailSelect */
							csrd.StoreID,
							csrd.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							sum(case when AmountTotal<>0 then 1 else 0 end) as ReceiptCount,
							0 as CheckCount,
							0 as DeclaredCoverCount,
							sum(case when csrd.ReceiptTypeID=1 then 1 else null end) as CashReceiptDivisor,
							sum(AmountTotal) as ReceiptAmount,
							0 as TipsPaidAmount,
							0 as TipsWithheldAmount,
							0 as PaidOutAmount,
							sum(-AmountTax) as TaxAmount,
							sum(-AmountDiscount) as DiscountAmount,
							0 as ServerCommission,
							sum(case when ReceiptTransactionTypeID=0 then (AmountFood+AmountBeverage+AmountOther+AmountCharge+AmountDiscount+AmountTax+AmountTip)-AmountTotal else 0 end) as GiftCardSold,
							sum(case when ReceiptTransactionTypeID=10 then (AmountFood+AmountBeverage+AmountOther+AmountCharge+AmountDiscount+AmountTax+AmountTip)-AmountTotal else 0 end) as ARPaidIn,
							sum(case when rt.ReceiptClass=4 then AmountTotal else 0 end) as GiftCardRedeemed,
							0 as PaidOut,
							sum(AmountTip) as TipsReceivedAmount,
							0 as TipsDeclaredAmount,
							sum(AmountOther+AmountCharge) as NonTipAmount,
							sum(AmountFood) as AmountFood,
							sum(AmountBeverage) as AmountBeverage,
							sum(AmountOther) as AmountOther,
							sum(AmountCharge) as AmountCharge,
							sum(AmountDiscount) as AmountDiscount,
							sum(AmountTax) as AmountTax,
							0 as AmountCreditCardSales,
							0 as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount"+
						(ReceiptDetailsCashTipsEarnedID!=0?@",
							sum(AmountNonTaxableSale) as AmountNonTaxableSale,
							sum(AmountNonTaxableCharge) as AmountNonTaxableCharge":@",
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge")+@"
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID" +
						OutputJoin() + @"
						where csrd.StoreID in (select StoreID from @tblStores)
							and csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and (csrd.AmountTotal<>0 or csrd.AmountDiscount<>0)
							and csrd.CheckNumber<>0
							and rt.ReceiptPostedAsDiscount=0" +
						OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
						OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
						OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
						OutputFilter("CashierStaffID",rv.CashierListCSV) +
						OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
						OutputFilter("ServerStaffID",rv.ServerListCSV) +
						OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
						sb.Append(@"
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount" +
							OutputGroupBy(Select.CommaModes.Before) + @"
						union all
						select
							csrd.StoreID,
							csrd.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter, true) + @"
							0 as ReceiptCount,
							0 as CheckCount,
							0 as DeclaredCoverCount,
							0 as CashReceiptDivisor,
							0 as ReceiptAmount,
							sum(case when pc.TipsWithheld=0 then -AmountTip-AmountTipCreditCardFee else 0 end) as TipsPaidAmount,
							sum(case when pc.TipsWithheld<>0 then -AmountTip-AmountTipCreditCardFee else 0 end) as TipsWithheldAmount,
							0 as PaidOutAmount,
							0 as TaxAmount,
							0 as DiscountAmount,
							sum(AmountTipCreditCardFee) as ServerCommission,
							0 as GiftCardSold,
							0 as ARPaidIn,
							0 as GiftCardRedeemed,
							0 as PaidOut,
							0 as TipsReceivedAmount,
							0 as TipsDeclaredAmount,
							0 as NonTipAmount,
							0 as AmountFood,
							0 as AmountBeverage,
							0 as AmountOther,
							0 as AmountCharge,
							0 as AmountDiscount,
							0 as AmountTax,
							sum(case when AmountTip<>0 then AmountFood+AmountBeverage+AmountDiscount else 0 end) as AmountCreditCardSales,
							sum(AmountTip) as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount,
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID
						inner join
							tblProfitCenters pc with (nolock)
						on pc.StoreID=csrd.StoreID
							and pc.ProfitCenterID=csrd.ProfitCenterID" +
						OutputJoin() + @"
						where csrd.StoreID in (select StoreID from @tblStores)
							and csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and (csrd.AmountTip<>0 or csrd.AmountTax<>0)
							and csrd.CheckNumber<>0
							and rt.ReceiptPostedAsDiscount=0" +
						OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
						OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
						OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
						OutputFilter("CashierStaffID",rv.CashierListCSV) +
						OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
						OutputFilter("ServerStaffID",rv.ServerListCSV) +
						OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
						sb.Append(@"
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount" +
							OutputGroupBy(Select.CommaModes.Before,true) + @"
						union all
						select
							csrd.StoreID,
							csrd.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							0 as ReceiptCount,
							0 as CheckCount,
							0 as DeclaredCoverCount,
							0 as CashReceiptDivisor,
							0 as ReceiptAmount,
							0 as TipsPaidAmount,
							0 as TipsWithheldAmount,
							0 as PaidOutAmount,
							0 as TaxAmount,
							sum(csrd.AmountTotal-(csrd.AmountDiscount+csrd.AmountTax)) as DiscountAmount,
							0 as ServerCommission,
							0 as GiftCardSold,
							0 as ARPaidIn,
							0 as GiftCardRedeemed,
							0 as PaidOut,
							0 as TipsReceivedAmount,
							0 as TipsDeclaredAmount,
							0 as NonTipAmount,
							0 as AmountFood,
							0 as AmountBeverage,
							0 as AmountOther,
							0 as AmountCharge,
							0 as AmountDiscount,
							0 as AmountTax,
							0 as AmountCreditCardSales,
							0 as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount,
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID" +
						OutputJoin() + @"
						where csrd.StoreID in (select StoreID from @tblStores)
							and csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and csrd.CheckNumber<>0
							and rt.ReceiptPostedAsDiscount=1" +
						OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
						OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
						OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
						OutputFilter("CashierStaffID",rv.CashierListCSV) +
						OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
						OutputFilter("ServerStaffID",rv.ServerListCSV) +
						OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
						sb.Append(@"
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount" +
							OutputGroupBy(Select.CommaModes.Before) + @"
						union all
						select
							csrd.StoreID,
							csrd.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							0 as ReceiptCount,
							0 as CheckCount,
							0 as DeclaredCoverCount,
							0 as CashReceiptDivisor,
							0 as ReceiptAmount,
							0 as TipsPaidAmount,
							0 as TipsWithheldAmount,
							0 as PaidOutAmount,
							0 as TaxAmount,
							0 as DiscountAmount,
							0 as ServerCommission,
							0 as GiftCardSold,
							0 as ARPaidIn,
							0 as GiftCardRedeemed,
							0 as PaidOut,
							0 as TipsReceivedAmount,
							sum(AmountTip) as TipsDeclaredAmount,
							0 as NonTipAmount,
							0 as AmountFood,
							0 as AmountBeverage,
							0 as AmountOther,
							0 as AmountCharge,
							0 as AmountDiscount,
							0 as AmountTax,
							0 as AmountCreditCardSales,
							0 as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount,
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID" +
						OutputJoin() + @"
						where csrd.StoreID in (select StoreID from @tblStores)
							and csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and " + (ReceiptDetailsCashTipsEarnedID==0?"csrd.ReceiptDetails='Cash Tips Earned'":"csrd.ReceiptsDetailID="+ReceiptDetailsCashTipsEarnedID.ToString()) +
						OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
						OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
						OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
						OutputFilter("CashierStaffID",rv.CashierListCSV) +
						OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
						OutputFilter("ServerStaffID",rv.ServerListCSV) +
						OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
						sb.Append(@"
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount" + 
							OutputGroupBy(Select.CommaModes.Before) + @"
						union all
						select
							csrd.StoreID,
							csrd.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							0 as ReceiptCount,
							0 as CheckCount,
							0 as DeclaredCoverCount,
							0 as CashReceiptDivisor,
							0 as ReceiptAmount,
							0 as TipsPaidAmount,
							0 as TipsWithheldAmount,
							sum(csrd.AmountTotal) as PaidOutAmount,
							0 as TaxAmount,
							0 as DiscountAmount,
							0 as ServerCommission,
							0 as GiftCardSold,
							0 as ARPaidIn,
							0 as GiftCardRedeemed,
							0 as PaidOut,
							0 as TipsReceivedAmount,
							0 as TipsDeclaredAmount,
							0 as NonTipAmount,
							0 as AmountFood,
							0 as AmountBeverage,
							0 as AmountOther,
							0 as AmountCharge,
							0 as AmountDiscount,
							0 as AmountTax,
							0 as AmountCreditCardSales,
							0 as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount,
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID" +
						OutputJoin() + @"
						where csrd.StoreID in (select StoreID from @tblStores)
							and csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and csrd.CheckNumber=0 and csrd.ReceiptTransactionTypeID in (-1,2)" +
						OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
						OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
						OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
						OutputFilter("CashierStaffID",rv.CashierListCSV) +
						OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
						OutputFilter("ServerStaffID",rv.ServerListCSV) +
						OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
						sb.Append(@"
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount" + 
							OutputGroupBy(Select.CommaModes.Before) + @"
						union all
						select
							csrd.StoreID,
							csrd.DayNumber,
							csrd.ReceiptClassID,
							csrd.ReceiptTypeID,
							csrd.CreditCardBatchNumber,
							csrd.CashierStaffDepartmentID,
							csrd.CashierStaffID,
							csrd.ServerStaffDepartmentID,
							csrd.ServerStaffID,
							csrd.CheckNumberID,
							csrd.CheckNumber,
							csrd.PersonNumber,
							csrd.ProfitCenterID,
							csrd.DayNameID,
							csrd.DayName,
							csrd.CashierSessionNumber,
							csrd.DayHourNumber,
							0 as ReceiptCount,
							count(csrd.CheckNumberCount) as CheckCount,
							0 as DeclaredCoverCount,
							0 as CashReceiptDivisor,
							0 as ReceiptAmount,
							0 as TipsPaidAmount,
							0 as TipsWithheldAmount,
							0 as PaidOutAmount,
							0 as TaxAmount,
							0 as DiscountAmount,
							0 as ServerCommission,
							0 as GiftCardSold,
							0 as ARPaidIn,
							0 as GiftCardRedeemed,
							0 as PaidOut,
							0 as TipsReceivedAmount,
							0 as TipsDeclaredAmount,
							0 as NonTipAmount,
							0 as AmountFood,
							0 as AmountBeverage,
							0 as AmountOther,
							0 as AmountCharge,
							0 as AmountDiscount,
							0 as AmountTax,
							0 as AmountCreditCardSales,
							0 as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount,
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge
						from (
							select distinct
								csrd.StoreID,
								csrd.DayNumber" +
								OutputSelectColumns(Select.CommaModes.BeforeAndAfter, TabCount+1) + @"
								csrd.CheckNumber as CheckNumberCount
							from
								tblCashAndSalesReceiptsDetail csrd with (nolock)
							inner join
								tblReceiptTypes rt with (nolock)
							on rt.StoreID=csrd.StoreID
								and rt.ReceiptTypeID=csrd.ReceiptTypeID" +
							OutputJoin() + @"
							where csrd.StoreID in (select StoreID from @tblStores)
								and csrd.DayNumber between @StartDayNumber and @EndDayNumber
								and csrd.CheckNumber<>0" +
							OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
							OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
							OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
							OutputFilter("CashierStaffID",rv.CashierListCSV) +
							OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
							OutputFilter("ServerStaffID",rv.ServerListCSV) +
							OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
							sb.Append(@"
							) csrd
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID
						inner join
							tblStores sto with (nolock)
						on sto.StoreID=csrd.StoreID
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount,
							csrd.ReceiptClassID,
							csrd.ReceiptTypeID,
							csrd.CreditCardBatchNumber,
							csrd.CashierStaffDepartmentID,
							csrd.CashierStaffID,
							csrd.ServerStaffDepartmentID,
							csrd.ServerStaffID,
							csrd.CheckNumberID,
							csrd.CheckNumber,
							csrd.PersonNumber,
							csrd.ProfitCenterID,
							csrd.DayNameID,
							csrd.DayName,
							csrd.CashierSessionNumber,
							csrd.DayHourNumber
						union all
						select
							csrd.StoreID,
							csrd.DayNumber" +
							OutputSelectColumns(Select.CommaModes.BeforeAndAfter) + @"
							0 as ReceiptCount,
							0 as CheckCount,
							sum(csrd.CoverCount) as DeclaredCoverCount,
							0 as CashReceiptDivisor,
							0 as ReceiptAmount,
							0 as TipsPaidAmount,
							0 as TipsWithheldAmount,
							0 as PaidOutAmount,
							0 as TaxAmount,
							0 as DiscountAmount,
							0 as ServerCommission,
							0 as GiftCardSold,
							0 as ARPaidIn,
							0 as GiftCardRedeemed,
							0 as PaidOut,
							0 as TipsReceivedAmount,
							0 as TipsDeclaredAmount,
							0 as NonTipAmount,
							0 as AmountFood,
							0 as AmountBeverage,
							0 as AmountOther,
							0 as AmountCharge,
							0 as AmountDiscount,
							0 as AmountTax,
							0 as AmountCreditCardSales,
							0 as AmountCreditCardTip,
							rt.ReceiptPostedAsDiscount,
							0 as AmountNonTaxableSale,
							0 as AmountNonTaxableCharge
						from
							tblCashAndSalesReceiptsDetail csrd with (nolock)
						inner join
							tblReceiptTypes rt with (nolock)
						on rt.StoreID=csrd.StoreID
							and rt.ReceiptTypeID=csrd.ReceiptTypeID" +
						OutputJoin() + @"
						left outer join (
							select distinct
								css.BatchID, cssd.CheckNumber
							from
								tblCashAndSalesSales css with (nolock)
							inner join tblCashAndSalesSalesDetail cssd with (nolock)
								on cssd.BatchID=css.BatchID and cssd.SaleItemID=css.SaleItemID
							inner join tblDiscounts dsc with (nolock)
								on dsc.StoreID=css.StoreID and dsc.DiscountID=css.Discount
							where css.StoreID in (select StoreID from @tblStores)
								and cssd.StoreID in (select StoreID from @tblStores)
								and css.DayNumber between @StartDayNumber and @EndDayNumber
								and cssd.DayNumber between @StartDayNumber and @EndDayNumber
								and cssd.CheckNumber<>0 and (dsc.EmployeeDiscount<>0 or dsc.VIPDiscount<>0)
							) qq
						on csrd.BatchID=qq.BatchID and csrd.CheckNumber=qq.CheckNumber
						where csrd.StoreID in (select StoreID from @tblStores)
							and csrd.DayNumber between @StartDayNumber and @EndDayNumber
							and csrd.CheckNumber<>0 and qq.CheckNumber is null" +
							OutputFilter("ProfitCenterID",rv.ProfitCenterListCSV) +
							OutputFilter("ReceiptTypeID",rv.ReceiptTypeListCSV) +
							OutputFilter("CashierStaffDepartmentID",rv.CashierStaffDepartmentListCSV) +
							OutputFilter("CashierStaffID",rv.CashierListCSV) +
							OutputFilter("ServerStaffDepartmentID",rv.ServerStaffDepartmentListCSV) +
							OutputFilter("ServerStaffID",rv.ServerListCSV) +
							OutputFilter("CheckNumberID",rv.CheckNumberListCSV));
							sb.Append(@"
						group by
							csrd.StoreID,
							csrd.DayNumber,
							rt.ReceiptPostedAsDiscount" + 
							OutputGroupBy(Select.CommaModes.BeforeAndAfter) + @"
							csrd.[Time]"+"\n");
					return sb.ToString().Replace("\n\t\t\t\t\t\t", TabString(TabCount));
					}
				public ReceiptsDetailSelect(DataAccessReceiptsReview rv, Int32 TabCount) {
					this.rv = rv;
					this.TabCount = TabCount;
					Select.Dimension dd = new Select.Dimension(this);
						Dimensions.Add("ProfitCenterID", dd);
						dd.Condition = (rv.ProfitCenterFieldExists && (rv.ProfitCenterFlag!=0));
						Select.KeyDataColumn kc = new Select.KeyDataColumn(dd);
							dd.KeyColumn = kc;
							kc.ColumnName = "ProfitCenterID";
							kc.KeyExpression = "csrd.ProfitCenterID";
					dd = new Select.Dimension(this);
						Dimensions.Add("ReceiptClassID", dd);
						dd.Condition = true; // (rv.ReceiptClassFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"ReceiptClassID","case when rt.ReceiptClass in (1,2) then rt.ReceiptClass else 3 end");
						dd.KeyColumn.KeyExpressionAlternate = "1";
						dd.KeyColumn.KeyExpressionAlternateConstant = true;
					dd = new Select.Dimension(this);
						Dimensions.Add("ReceiptTypeID", dd);
						dd.Condition = true; /* (rv.ReceiptTypeFlag!=0); */
						dd.KeyColumn = new Select.KeyDataColumn(dd,"ReceiptTypeID","csrd.StoreID","csrd.ReceiptTypeID");
						dd.KeyColumn.KeyExpressionAlternate = "1";
						dd.KeyColumn.KeyExpressionAlternateConstant = true;
					dd = new Select.Dimension(this);
						Dimensions.Add("CreditCardBatchNumber", dd);
						dd.Condition = (rv.CreditCardBatchNumberFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"CreditCardBatchNumber","csrd.StoreID","isnull(csrd.CreditCardBatchNumber,0)");
					dd = new Select.Dimension(this);
						Dimensions.Add("CashierStaffDepartmentID", dd);
						dd.Condition = (rv.CashierStaffDepartmentFlag!=0 || rv.CashierStaffDepartmentListCSV.Length>0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"CashierStaffDepartmentID","csrd.StoreID","isnull(CashierStaffDepartmentID,0)");
						Select.DataColumn.Join jn = new Select.DataColumn.Join();
							dd.KeyColumn.Joins.Add(jn);
							jn.JoinType = Select.DataColumn.Join.JoinTypes.LeftOuter;
							jn.TableExpression = "(select cste.BatchID, cste.TimeEntryItemID, cste.StoreID, cste.StaffDepartmentID as CashierStaffDepartmentID from tblCashAndSalesTimeEntries cste where cste.StoreID in (select StoreID from @tblStores) and cste.DayNumber between @StartDayNumber and @EndDayNumber) cste";
							jn.JoinExpression = "csrd.BatchID=cste.BatchID and csrd.CashierTimeEntryItemID=cste.TimeEntryItemID";
					dd = new Select.Dimension(this);
						Dimensions.Add("CashierStaffID", dd);
						dd.Condition = (rv.CashierFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"CashierStaffID","csrd.StoreID","csrd.CashierStaffID");
					dd = new Select.Dimension(this);
						Dimensions.Add("ServerStaffDepartmentID", dd);
						dd.Condition = (rv.ServerStaffDepartmentFlag!=0 || rv.ServerStaffDepartmentListCSV.Length>0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"ServerStaffDepartmentID","csrd.StoreID","csrd.ServerStaffDepartmentID");
					dd = new Select.Dimension(this);
						Dimensions.Add("ServerStaffID", dd);
						dd.Condition = (rv.ServerFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"ServerStaffID","csrd.StoreID","csrd.ServerStaffID");
					dd = new Select.Dimension(this);
						Dimensions.Add("CheckNumberID", dd);
						dd.Condition = (rv.CheckNumberFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"CheckNumberID","csrd.StoreID","cast(sto.StoreNumber as bigint)*10000000000+csrd.CheckNumber*100+csrd.PersonNumber");
						Select.DataColumn dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "CheckNumber";
							dc.ThenExpression = "csrd.CheckNumber";
						dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "PersonNumber";
							dc.ThenExpression = "csrd.PersonNumber";
						jn = new Select.DataColumn.Join();
							dd.KeyColumn.Joins.Add(jn);
							jn.JoinType = Select.DataColumn.Join.JoinTypes.LeftOuter;
							jn.TableExpression = "@tblStores sto";
							jn.JoinExpression = "sto.StoreID=csrd.StoreID";
					dd = new Select.Dimension(this);
						Dimensions.Add("DayNameID", dd);
						dd.Condition = (rv.DayNameFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"DayNameID","csrd.DayNumber");
						dc = new Select.DataColumn(dd);
							dd.DataColumns.Add(dc);
							dc.ColumnName = "DayName";
							dc.ThenExpression = "substring(cast(dbo.fn_CalcDayNumberDate(csrd.DayNumber) as varchar(20)),1,3)+' '+cast(day(dbo.fn_CalcDayNumberDate(csrd.DayNumber)) as varchar(10))";
					dd = new Select.Dimension(this);
						Dimensions.Add("CashierSessionNumber", dd);
						dd.Condition = (rv.CashierSessionNumberFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"CashierSessionNumber","CashierSessionNumber");
					dd = new Select.Dimension(this);
						Dimensions.Add("DayHourNumber", dd);
						dd.Condition = (rv.DayHourNumberFlag!=0);
						dd.KeyColumn = new Select.KeyDataColumn(dd,"DayHourNumber","floor(datediff(minute,'1900-01-01',convert(datetime,csrd.[Time]))/60)");
					}
				}
			}
		public SelectParameterList SelectParameters = new SelectParameterList();
		private String RetrieveReceiptsDataSQL(Boolean blnProfitCenterFieldExists, Boolean ShowDepositsFromDetail, Int32 ReceiptDetailsCashTipsEarnedID) {
			StringBuilder sb = new StringBuilder();
			SelectParameters.Add(new SelectParameterList.Parameter("StartDayNumber","int",StartDayNumber.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("EndDayNumber","int",EndDayNumber.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("StoreGroupFlag","bit",StoreGroupFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("StoreGroupListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(StoreGroupListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("StoreFlag","bit",StoreFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("StoreListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(StoreListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("ProfitCenterFlag","bit",ProfitCenterFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("ProfitCenterListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(ProfitCenterListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("ReceiptClassFlag","bit",ReceiptClassFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("ReceiptClassListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(ReceiptClassListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("ReceiptTypeFlag","bit",ReceiptTypeFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("ReceiptTypeListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(ReceiptTypeListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("CreditCardBatchNumberFlag","bit",CreditCardBatchNumberFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("CreditCardBatchNumberListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(CreditCardBatchNumberListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("CashierStaffDepartmentFlag","bit",CashierStaffDepartmentFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("CashierStaffDepartmentListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(CashierStaffDepartmentListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("CashierFlag","bit",CashierFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("CashierListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(CashierListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("ServerStaffDepartmentFlag","bit",ServerStaffDepartmentFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("ServerStaffDepartmentListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(ServerStaffDepartmentListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("ServerFlag","bit",ServerFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("ServerListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(ServerListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("CheckNumberFlag","bit",CheckNumberFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("CheckNumberListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(CheckNumberListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("DayNameFlag","bit",DayNameFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("DayNameListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(DayNameListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("WeekNameFlag","bit",WeekNameFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("WeekNameListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(WeekNameListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("CashierSessionNumberFlag","bit",CashierSessionNumberFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("CashierSessionNumberListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(CashierSessionNumberListCSV.ToString())));
			SelectParameters.Add(new SelectParameterList.Parameter("DayHourNumberFlag","bit",DayHourNumberFlag.ToString()));
			SelectParameters.Add(new SelectParameterList.Parameter("DayHourNumberListCSV","varchar(8000)",SelectParameterList.SingleQuoteString(DayHourNumberListCSV.ToString())));
			sb.Append(SelectParameters.ToString());
			ReceiptsSelect ReceiptsSelect = new ReceiptsSelect(this);
			ReceiptsSelect.ReceiptDetailsCashTipsEarnedID = ReceiptDetailsCashTipsEarnedID;
			ReceiptsSelect.OutputDetailFields = ShowDepositsFromDetail;
			sb.Append(this.SQLSelectStatement = ReceiptsSelect.OutputSelect());
			return sb.ToString();
			}
		private String RetrieveReceiptsHeaderSQL() {
			StringBuilder sb = new StringBuilder();
			sb.Append(@"
			declare @StartDayNumber int
			declare @EndDayNumber int
			declare @StoreGroupFlag bit
			declare @StoreGroupListCSV varchar(8000)
			declare @StoreFlag bit
			declare @StoreListCSV varchar(8000)
			declare @ProfitCenterFlag bit
			declare @ProfitCenterListCSV varchar(8000)
			declare @ReceiptClassFlag bit
			declare @ReceiptClassListCSV varchar(8000)
			declare @ReceiptTypeFlag bit
			declare @ReceiptTypeListCSV varchar(8000)
			declare @CreditCardBatchNumberFlag bit
			declare @CreditCardBatchNumberListCSV varchar(8000)
			declare @CashierStaffDepartmentFlag bit
			declare @CashierStaffDepartmentListCSV varchar(8000)
			declare @CashierFlag bit
			declare @CashierListCSV varchar(8000)
			declare @ServerStaffDepartmentFlag bit
			declare @ServerStaffDepartmentListCSV varchar(8000)
			declare @ServerFlag bit
			declare @ServerListCSV varchar(8000)
			declare @CheckNumberFlag bit
			declare @CheckNumberListCSV varchar(8000)
			declare @DayNameFlag bit
			declare @DayNameListCSV varchar(8000)
			declare @WeekNameFlag bit
			declare @WeekNameListCSV varchar(8000)
			declare @CashierSessionNumberFlag bit
			declare @CashierSessionNumberListCSV varchar(8000)
			declare @DayHourNumberFlag bit
			declare @DayHourNumberListCSV varchar(8000)"+"\n");
			sb.Append(
					@"			set @StartDayNumber=" + StartDayNumber.ToString()+"\n"
				+ @"			set @EndDayNumber=" + EndDayNumber.ToString()+"\n"
				+ @"			set @StoreGroupFlag=" + StoreGroupFlag.ToString()+"\n"
				+ @"			set @StoreGroupListCSV='" + StoreGroupListCSV.ToString()+"'\n"
				+ @"			set @StoreFlag=" + StoreFlag.ToString()+"\n"
				+ @"			set @StoreListCSV='" + StoreListCSV.ToString()+"'\n"
				+ @"			set @ProfitCenterFlag=" + ProfitCenterFlag.ToString()+"\n"
				+ @"			set @ProfitCenterListCSV='" + ProfitCenterListCSV.ToString()+"'\n"
				+ @"			set @ReceiptClassFlag=" + ReceiptClassFlag.ToString()+"\n"
				+ @"			set @ReceiptClassListCSV='" + ReceiptClassListCSV.ToString()+"'\n"
				+ @"			set @ReceiptTypeFlag=" + ReceiptTypeFlag.ToString()+"\n"
				+ @"			set @ReceiptTypeListCSV='" + ReceiptTypeListCSV.ToString()+"'\n"
				+ @"			set @CreditCardBatchNumberFlag=" + CreditCardBatchNumberFlag.ToString()+"\n"
				+ @"			set @CreditCardBatchNumberListCSV='" + CreditCardBatchNumberListCSV.ToString()+"'\n"
				+ @"			set @CashierStaffDepartmentFlag=" + CashierStaffDepartmentFlag.ToString()+"\n"
				+ @"			set @CashierStaffDepartmentListCSV='" + CashierStaffDepartmentListCSV.ToString()+"'\n"
				+ @"			set @CashierFlag=" + CashierFlag.ToString()+"\n"
				+ @"			set @CashierListCSV='" + CashierListCSV.ToString()+"'\n"
				+ @"			set @ServerStaffDepartmentFlag=" + ServerStaffDepartmentFlag.ToString()+"\n"
				+ @"			set @ServerStaffDepartmentListCSV='" + ServerStaffDepartmentListCSV.ToString()+"'\n"
				+ @"			set @ServerFlag=" + ServerFlag.ToString()+"\n"
				+ @"			set @ServerListCSV='" + ServerListCSV.ToString()+"'\n"
				+ @"			set @CheckNumberFlag=" + CheckNumberFlag.ToString()+"\n"
				+ @"			set @CheckNumberListCSV='" + CheckNumberListCSV.ToString()+"'\n"
				+ @"			set @DayNameFlag=" + DayNameFlag.ToString()+"\n"
				+ @"			set @DayNameListCSV='" + DayNameListCSV.ToString()+"'\n"
				+ @"			set @WeekNameFlag=" + WeekNameFlag.ToString()+"\n"
				+ @"			set @WeekNameListCSV='" + WeekNameListCSV.ToString()+"'\n"
				+ @"			set @CashierSessionNumberFlag=" + CashierSessionNumberFlag.ToString()+"\n"
				+ @"			set @CashierSessionNumberListCSV='" + CashierSessionNumberListCSV.ToString()+"'\n"
				+ @"			set @DayHourNumberFlag=" + DayHourNumberFlag.ToString()+"\n"
				+ @"			set @DayHourNumberListCSV='" + DayHourNumberListCSV.ToString()+"'\n");
			sb.Append(@"			declare @table_variable TABLE (
				StoreGroupName varchar(500),
				StoreName varchar(700),
				ProfitCenter varchar(500),
				ReceiptClass varchar(200),
				ReceiptType varchar(700),
				CreditCardBatchNumber varchar(300),
				CashierStaffDepartmentName varchar(700),
				CashierName varchar(900),
				ServerStaffDepartmentName varchar(700),
				ServerName varchar(800),
				CheckNumber varchar(800),
				DayName varchar(330),
				WeekName varchar(330),
				DayTimeSlotName varchar(400),
				CashierSessionNumber int,
				DayHourNumber int,
				StoreGroupDetailFlag bit,
				StoreDetailFlag bit,
				ProfitCenterDetailFlag bit,
				ReceiptClassDetailFlag bit,
				ReceiptTypeDetailFlag bit,
				CreditCardBatchNumberDetailFlag bit,
				CashierStaffDepartmentDetailFlag bit,
				CashierDetailFlag bit,
				ServerDetailFlag bit,
				CheckNumberDetailFlag bit,
				DayNameDetailFlag bit,
				WeekNameDetailFlag bit,
				DayTimeSlotDetailFlag bit,
				CashierSessionNumberDetailFlag bit,
				DayHourNumberDetailFlag bit,
				StoreGroupMultipleFlag bit,
				StoreMultipleFlag bit,
				ProfitCenterMultipleFlag bit,
				ReceiptClassMultipleFlag bit,
				ReceiptTypeMultipleFlag bit,
				CreditCardBatchNumberMultipleFlag bit,
				CashierStaffDepartmentMultipleFlag bit,
				CashierMultipleFlag bit,
				ServerMultipleFlag bit,
				CheckNumberMultipleFlag bit,
				DayNameMultipleFlag bit,
				WeekNameMultipleFlag bit,
				DayTimeSlotMultipleFlag bit,
				CashierSessionNumberMultipleFlag bit,
				DayHourNumberMultipleFlag bit
				)
			declare @StoreGroupList varchar(1000)
			declare @StoreGroupDetailFlag bit
			declare @StoreGroupMultipleFlag bit
			declare @StoreNameList varchar(1000)
			declare @StoreDetailFlag bit
			declare @StoreMultipleFlag bit
			declare @ProfitCenterNameList varchar(1000)
			declare @ProfitCenterDetailFlag bit
			declare @ProfitCenterMultipleFlag bit
			declare @ReceiptClassNameList varchar(1000)
			declare @ReceiptClassDetailFlag bit
			declare @ReceiptClassMultipleFlag bit
			declare @ReceiptTypeNameList varchar(1000)
			declare @ReceiptTypeDetailFlag bit
			declare @ReceiptTypeMultipleFlag bit
			declare @CreditCardBatchNumberNameList varchar(1000)
			declare @CreditCardBatchNumberDetailFlag bit
			declare @CreditCardBatchNumberMultipleFlag bit
			declare @CashierStaffDepartmentNameList varchar(1000)
			declare @CashierStaffDepartmentDetailFlag bit
			declare @CashierStaffDepartmentMultipleFlag bit
			declare @CashierNameList varchar(1000)
			declare @CashierDetailFlag bit
			declare @CashierMultipleFlag bit
			declare @ServerStaffDepartmentNameList varchar(1000)
			declare @ServerStaffDepartmentDetailFlag bit
			declare @ServerStaffDepartmentMultipleFlag bit
			declare @ServerNameList varchar(1000)
			declare @ServerDetailFlag bit
			declare @ServerMultipleFlag bit
			declare @CheckNumberNameList varchar(1000)
			declare @CheckNumberDetailFlag bit
			declare @CheckNumberMultipleFlag bit
			declare @DayNameNameList varchar(500)
			declare @DayNameDetailFlag bit
			declare @DayNameMultipleFlag bit
			declare @WeekNameNameList varchar(500)
			declare @WeekNameDetailFlag bit
			declare @WeekNameMultipleFlag bit
			declare @CashierSessionNumberNameList varchar(500)
			declare @CashierSessionNumberDetailFlag bit
			declare @CashierSessionNumberMultipleFlag bit
			declare @DayHourNumberNameList varchar(500)
			declare @DayHourNumberDetailFlag bit
			declare @DayHourNumberMultipleFlag bit
			set @StoreGroupDetailFlag = 0
			set @StoreDetailFlag = 0
			set @ProfitCenterDetailFlag = 0
			set @ReceiptClassDetailFlag = 0
			set @ReceiptTypeDetailFlag = 0
			set @CashierStaffDepartmentDetailFlag = 0
			set @CashierDetailFlag = 0
			set @ServerStaffDepartmentDetailFlag = 0
			set @ServerDetailFlag = 0
			set @CheckNumberDetailFlag = 0
			set @DayNameDetailFlag = 0
			set @WeekNameDetailFlag = 0
			set @StoreGroupMultipleFlag = 0
			set @StoreMultipleFlag = 0
			set @ProfitCenterMultipleFlag = 0
			set @ReceiptClassMultipleFlag = 0
			set @ReceiptTypeMultipleFlag = 0
			set @CashierStaffDepartmentMultipleFlag = 0
			set @CashierMultipleFlag = 0
			set @ServerStaffDepartmentMultipleFlag = 0
			set @ServerMultipleFlag = 0
			set @CheckNumberMultipleFlag = 0
			set @DayNameMultipleFlag = 0
			set @WeekNameMultipleFlag = 0
			set @CashierSessionNumberDetailFlag = 0
			set @DayHourNumberDetailFlag = 0
			declare @TotalCount int
			declare @tblStoreGroups table (StoreGroupID int, StoreGroupName varchar(50), StoreGroupNumber int)
			if len(@StoreGroupListCSV)>0
				begin
				insert into @tblStoreGroups
					select KeyValue, StoreGroupName, StoreGroupNumber
				from
					tblStoreGroups sg with (nolock)
				inner join
					fn_CreateKeyTableValueFromCSVString(@StoreGroupListCSV) csv
				on csv.KeyValue = sg.StoreGroupID
				set @TotalCount = (select count(*) as sgCount from @tblStoreGroups sg)
				set @StoreGroupMultipleFlag = case when (@TotalCount>1) then 1 else 0 end
				set @StoreGroupDetailFlag = case when (@TotalCount>8) then 1 else 0 end
				end
			else
				insert into @tblStoreGroups(StoreGroupID, StoreGroupName, StoreGroupNumber)
					select distinct StoreGroupID, StoreGroupName, StoreGroupNumber from tblStoreGroups
			declare @StoreGroupNameList varchar(1000)
			set @StoreGroupNameList = null
			declare @StoreGroupID int
			declare @StoreGroupName varchar(50)
			declare @StoreGroupNumber int
			declare cur cursor for select distinct StoreGroupID, StoreGroupName, StoreGroupNumber from @tblStoreGroups order by StoreGroupNumber
			open cur
			fetch next from cur into @StoreGroupID, @StoreGroupName, @StoreGroupNumber
			while @@fetch_status=0
				begin
				if @StoreGroupNameList is null
					set @StoreGroupNameList = ''
				else
					if  len(@StoreGroupNameList)<995
						set @StoreGroupNameList = @StoreGroupNameList + '<br/>'
				set @StoreGroupNameList = left(@StoreGroupNameList + rtrim(isnull(@StoreGroupName,'')),1000)
				fetch next from cur into @StoreGroupID, @StoreGroupName, @StoreGroupNumber
				end
			close cur
			deallocate cur

			declare @tblStores table(StoreID int, StoreName varchar(50), StoreGCode varchar(50), StoreNumber int)
			if len(@StoreListCSV)>0
				begin
				insert into @tblStores
				select KeyValue, isnull(tblStores.StoreReportName,tblStores.StoreName) as StoreName, tblStores.StoreGCode, tblStores.StoreNumber
				from
					tblStores
				inner join
					fn_CreateKeyTableValueFromCSVString(@StoreListCSV) csv
				on csv.KeyValue=tblStores.StoreID
				set @TotalCount = (select count(*) as sgCount from @tblStores sg)
				set @StoreMultipleFlag = case when (@TotalCount>1) then 1 else 0 end
				set @StoreDetailFlag = case when (@TotalCount>8) then 1 else 0 end
				end
			else 
				insert into @tblStores(StoreID, StoreName, StoreGCode, StoreNumber)
				select distinct StoreID, isnull(tblStores.StoreReportName,tblStores.StoreName) as StoreName, StoreGCode, StoreNumber from tblStores where Active=1
			set @StoreNameList = null
			declare @StoreID int
			declare @StoreName varchar(50)
			declare @StoreNumber int
			declare @StoreGCode varchar(50)
			if @StoreDetailFlag=0
				if len(@StoreListCSV)=0
					set @StoreNameList = 'All'
				else
					begin
					declare cur cursor for select distinct StoreID, StoreName, StoreGCode, StoreNumber from @tblStores order by StoreNumber
					open cur
					if @@cursor_rows>8
						set @StoreDetailFlag = 1
					fetch next from cur into @StoreID, @StoreName, @StoreGCode, @StoreNumber
					while @@fetch_status=0
						begin
						if @StoreNameList is null
							set @StoreNameList = ''
						else
							if  len(@StoreNameList)<995
								if @StoreDetailFlag=1
									set @StoreNameList = @StoreNameList + ', '
								else
									set @StoreNameList = @StoreNameList + '<br/>'
						set @StoreNameList = left(@StoreNameList + rtrim(isnull(@StoreName,''))+isnull(' ('+rtrim(@StoreGCode)+')',''),1000)
						fetch next from cur into @StoreID, @StoreName, @StoreGCode, @StoreNumber
						end
					close cur
					deallocate cur
					end

			declare @tblProfitCenters table(StoreID int, ProfitCenterID int, ProfitCenter varchar(50))
			if len(@ProfitCenterListCSV)>0
				begin
				insert into @tblProfitCenters
				select
					tblProfitCenters.StoreID,
					ProfitCenterID,
					ProfitCenter
				from
					tblProfitCenters
				inner join
					@tblStores tblStores
				on tblProfitCenters.StoreID=tblStores.StoreID" + "\n");
			if (ProfitCenterListCSV.Length>0)
				sb.Append ("\t\t\t\twhere (" + ParsePairSetOutput(ProfitCenterListCSV,"tblProfitCenters.StoreID","tblProfitCenters.ProfitCenterID") + ")");
			sb.Append(@"
				set @TotalCount = (select count(*) as rtCount from @tblProfitCenters rt)
				end
			else
				insert into @tblProfitCenters
				select
					tblProfitCenters.StoreID,
					ProfitCenterID,
					ProfitCenter
				from
					tblProfitCenters
				inner join
					@tblStores tblStores
				on tblProfitCenters.StoreID=tblStores.StoreID
			set @ProfitCenterNameList = null
			if @ProfitCenterFlag=0
				if len(@ProfitCenterListCSV)=0
					set @ProfitCenterNameList = 'All'
				else
					begin
					declare @ProfitCenter varchar(50)
					declare cur cursor for
						select distinct ProfitCenter
						from @tblProfitCenters
						order by ProfitCenter
					open cur
					if @@cursor_rows>8
						set @ProfitCenterDetailFlag = 1
					if @@cursor_rows>1
						set @ProfitCenterMultipleFlag = 1
					fetch next from cur into @ProfitCenter
					while @@fetch_status=0
						begin
						if @ProfitCenterNameList is null
							set @ProfitCenterNameList = ''
						else
							if len(@ProfitCenterNameList)<995
								if @ProfitCenterDetailFlag=1
									set @ProfitCenterNameList = @ProfitCenterNameList + ', '
								else
									set @ProfitCenterNameList = @ProfitCenterNameList + '<br/>'
						set @ProfitCenterNameList = left(@ProfitCenterNameList + rtrim(isnull(@ProfitCenter,'')),1000)
						fetch next from cur into @ProfitCenter
						end
					close cur
					deallocate cur
					end

			declare @tblReceiptClasses table (TableKey int, ReceiptClass varchar(50))
			if len(@ReceiptClassListCSV)>0
				begin
				insert into @tblReceiptClasses (TableKey, ReceiptClass)
					select
						KeyValue,
							case KeyValue
								when 1 then 'Cash'
								when 2 then 'Credit Card'
								else 'Other'
							end
						as ReceiptClass
					from
						fn_CreateKeyTableValueFromCSVString(@ReceiptClassListCSV) rcl
				end
			else
				begin
				insert into @tblReceiptClasses (TableKey, ReceiptClass) values (1,'Cash')
				insert into @tblReceiptClasses (TableKey, ReceiptClass) values (2,'Credit Card')
				insert into @tblReceiptClasses (TableKey, ReceiptClass) values (3,'Other')
				end
			set @ReceiptClassNameList = null
			if @ReceiptClassFlag=0
				if len(@ReceiptClassListCSV)=0
					set @ReceiptClassNameList = 'All'
				else
					begin
					declare @ReceiptClass varchar(50)
					declare cur cursor for
						select ReceiptClass from @tblReceiptClasses order by TableKey
					open cur
					if @@cursor_rows>8
						set @ReceiptClassDetailFlag = 1
					if @@cursor_rows>1
						set @ReceiptClassMultipleFlag = 1
					fetch next from cur into @ReceiptClass
					while @@fetch_status=0
						begin
						if @ReceiptClassNameList is null
							set @ReceiptClassNameList = ''
						else
							if len(@ReceiptClassNameList)<995
								if @ReceiptClassDetailFlag=1
									set @ReceiptClassNameList = @ReceiptClassNameList + ', '
								else
									set @ReceiptClassNameList = @ReceiptClassNameList + '<br/>'
						set @ReceiptClassNameList = left(@ReceiptClassNameList + rtrim(isnull(@ReceiptClass,'')),1000)
						fetch next from cur into @ReceiptClass
						end
					close cur
					deallocate cur
					end

			declare @tblReceiptTypes table(StoreID int, ReceiptTypeID int, ReceiptType varchar(50), ReceiptTypeSort varchar(6), ReceiptClass int)
			if len(@ReceiptTypeListCSV)>0
				begin
				insert into @tblReceiptTypes
				select
					tblReceiptTypes.StoreID,
					ReceiptTypeID,
					ReceiptType,
					ReceiptTypeID as ReceiptTypeSort,
					ReceiptClass
				from
					tblReceiptTypes
				inner join
					@tblStores tblStores
				on tblReceiptTypes.StoreID=tblStores.StoreID" + "\n");
			if (ReceiptTypeListCSV.Length>0)
				sb.Append ("\t\t\t\twhere (" + ParsePairSetOutput(ReceiptTypeListCSV,"tblReceiptTypes.StoreID","tblReceiptTypes.ReceiptTypeID") + ")");
			sb.Append(@"
				set @TotalCount = (select count(*) as rtCount from @tblReceiptTypes rt)
				end
			else
				insert into @tblReceiptTypes
				select
					tblReceiptTypes.StoreID,
					ReceiptTypeID,
					ReceiptType,
					ReceiptTypeID as ReceiptTypeSort,
					ReceiptClass
				from
					tblReceiptTypes
				inner join
					@tblStores tblStores
				on tblReceiptTypes.StoreID=tblStores.StoreID
			set @ReceiptTypeNameList = null
			if @ReceiptTypeFlag=0
				if len(@ReceiptTypeListCSV)=0
					set @ReceiptTypeNameList = 'All'
				else
					begin
					declare @ReceiptType varchar(50)
					declare @ReceiptTypeSort varchar(6)
					declare cur cursor for
						select distinct ReceiptType, min(cast(ReceiptTypeSort as int)) as ReceiptTypeSort
						from @tblReceiptTypes
						group by ReceiptTypeID, ReceiptType
						order by min(cast(ReceiptTypeSort as int))
					open cur
					if @@cursor_rows>8
						set @ReceiptTypeDetailFlag = 1
					if @@cursor_rows>1
						set @ReceiptTypeMultipleFlag = 1
					fetch next from cur into @ReceiptType, @ReceiptTypeSort
					while @@fetch_status=0
						begin
						if @ReceiptTypeNameList is null
							set @ReceiptTypeNameList = ''
						else
							if len(@ReceiptTypeNameList)<995
								if @ReceiptTypeDetailFlag=1
									set @ReceiptTypeNameList = @ReceiptTypeNameList + ', '
								else
									set @ReceiptTypeNameList = @ReceiptTypeNameList + '<br/>'
						set @ReceiptTypeNameList = left(@ReceiptTypeNameList + rtrim(isnull(@ReceiptType,'')),1000)
						fetch next from cur into @ReceiptType, @ReceiptTypeSort
						end
					close cur
					deallocate cur
					end
			declare @CashierStaffDepartmentSelectTable table (KeyValue int, StoreID int, StaffDepartmentName varchar(50), StaffDepartmentSort varchar(6))
			if len(@CashierStaffDepartmentListCSV)>0
				begin
				insert into @CashierStaffDepartmentSelectTable
					select
						StaffDepartmentID as KeyValue,
						tblStores.StoreID,
						StaffDepartmentName,
						StaffDepartmentSort
					from
						tblStaffDepartments
					inner join
						@tblStores tblStores
					on tblStaffDepartments.StoreID=tblStores.StoreID");
			if (CashierStaffDepartmentListCSV.Length>0)
				sb.Append ("\n\t\t\t\t\twhere (" + ParsePairSetOutput(CashierStaffDepartmentListCSV,"tblStaffDepartments.StoreID","tblStaffDepartments.StaffDepartmentID") + ")");
			sb.Append(@"
				end
			else
				insert into @CashierStaffDepartmentSelectTable
					select
						StaffDepartmentID as KeyValue,
						tblStores.StoreID,
						StaffDepartmentName,
						StaffDepartmentSort
					from
						tblStaffDepartments
					inner join
						@tblStores tblStores
					on tblStaffDepartments.StoreID=tblStores.StoreID
			insert into @CashierStaffDepartmentSelectTable (KeyValue, StoreID, StaffDepartmentName, StaffDepartmentSort)
				select 0, sto.StoreID, ' [Not Applicable]' as StaffDepartmentName, '0000 ' as StaffDepartmentSort from @tblStores sto");
			if (CashierStaffDepartmentListCSV.Length>0)
				sb.Append ("\n\t\t\t\t\twhere (" + ParsePairSetOutput(CashierStaffDepartmentListCSV,"sto.StoreID","0") + ")");
			sb.Append(@"
			declare @CashierStaffDepartmentID int
			set @CashierStaffDepartmentNameList = null
			if @CashierStaffDepartmentFlag=0
				if len(@CashierStaffDepartmentListCSV)=0
					set @CashierStaffDepartmentNameList = 'All'
				else
					begin
					declare @CashierStaffDepartmentName varchar(50)
					declare @CashierStaffDepartmentSort varchar(6)
					declare cur cursor for
						select distinct
							dst.KeyValue,
							dst.StaffDepartmentName as CashierStaffDepartmentName,
							min(dst.StaffDepartmentSort) as CashierStaffDepartmentSort
						from
							@CashierStaffDepartmentSelectTable dst
						inner join
							@tblStores sst
						on dst.StoreID=sst.StoreID
						group by dst.KeyValue, dst.StaffDepartmentName
						order by min(dst.StaffDepartmentSort)
					open cur
					fetch next from cur into @CashierStaffDepartmentID, @CashierStaffDepartmentName, @CashierStaffDepartmentSort
					while @@fetch_status=0
						begin
						if @CashierStaffDepartmentNameList is null
							set @CashierStaffDepartmentNameList = ''
						else
							if len(@CashierStaffDepartmentNameList)<995
								set @CashierStaffDepartmentNameList = @CashierStaffDepartmentNameList + '<br/>'
						set @CashierStaffDepartmentNameList = left(@CashierStaffDepartmentNameList + rtrim(isnull(@CashierStaffDepartmentName,'')),1000)
						fetch next from cur into @CashierStaffDepartmentID, @CashierStaffDepartmentName, @CashierStaffDepartmentSort
						end
					close cur
					deallocate cur
					end
			declare @tblStaffCashierStaffDepartmentNames table (TableKey bigint primary key, StoreID int, StaffDepartmentID int, StaffDepartmentName varchar(100), StaffDepartmentSort varchar(100))
			declare @tblStaffCashiers table (TableKey bigint, StoreID int, StoreStaffID int, StoreStaff varchar(100))
			declare @tblStaffServers table (TableKey bigint, StoreID int, StoreStaffID int, StoreStaff varchar(100))");
			if (CashierStaffDepartmentListCSV.Length>0)
				sb.Append(@"
				insert into @tblStaffCashierStaffDepartmentNames (TableKey, StoreID, StaffDepartmentID, StaffDepartmentName)
				select
					sd.StoreID*4294967296+sd.StaffDepartmentID as TableKey, sd.StoreID, sd.StaffDepartmentID, sd.StaffDepartmentName
				from
					tblStaffDepartments sd
				inner join
					@tblStores sto
				on sto.StoreID=sd.StoreID
				inner join (
					select StoreID, StaffDepartmentID from tblStaffDepartments sd where (" + ParsePairSetOutput(CashierStaffDepartmentListCSV,"sd.StoreID","sd.StaffDepartmentID") + @")
					) srt
				on srt.StoreID=sd.StoreID
					and srt.StaffDepartmentID = sd.StaffDepartmentID");
			else		
				sb.Append(@"
				insert into @tblStaffCashierStaffDepartmentNames (TableKey, StoreID, StaffDepartmentID, StaffDepartmentName)
				select
					sd.StoreID*4294967296+sd.StaffDepartmentID as TableKey, sd.StoreID, sd.StaffDepartmentID, sd.StaffDepartmentName
				from
					tblStaffDepartments sd
				inner join
					@tblStores sto
				on sto.StoreID=sd.StoreID");
			sb.Append(@"
				insert into @tblStaffCashierStaffDepartmentNames (TableKey, StoreID, StaffDepartmentID, StaffDepartmentName)
					select sto.StoreID*4294967296+0, sto.StoreID, 0 as StaffDepartmentID, ' [Not Applicable]' as StaffDepartmentName from @tblStores sto
				");
			if (CashierListCSV.Length>0)
				sb.Append(@"
				insert into @tblStaffCashiers
				select
					stf.StoreID*4294967296+stf.StoreStaffID as TableKey, stf.StoreID, stf.StoreStaffID, isnull(rtrim(stf.LastName)+', ','')+isnull(rtrim(stf.FirstName),'') as StaffName
				from
					tblStaff stf
				inner join
					@tblStores sto
				on sto.StoreID=stf.StoreID
				inner join (
					select StoreID, StoreStaffID from tblStaff stf where (" + ParsePairSetOutput(CashierListCSV,"stf.StoreID","stf.StoreStaffID") + @")
					) srt
				on srt.StoreID=stf.StoreID
					and srt.StoreStaffID = stf.StoreStaffID");
			else		
				sb.Append(@"
				insert into @tblStaffCashiers
				select
					stf.StoreID*4294967296+stf.StoreStaffID as TableKey, stf.StoreID, stf.StoreStaffID, isnull(rtrim(stf.LastName)+', ','')+isnull(rtrim(stf.FirstName),'') as StaffName
				from
					tblStaff stf
				inner join
					@tblStores sto
				on sto.StoreID=stf.StoreID");
			if (ServerListCSV.Length>0)
				sb.Append(@"
				insert into @tblStaffServers
				select
					stf.StoreID*4294967296+stf.StoreStaffID as TableKey, stf.StoreID, stf.StoreStaffID, isnull(rtrim(stf.LastName)+', ','')+isnull(rtrim(stf.FirstName),'') as StaffName
				from
					tblStaff stf
				inner join
					@tblStores sto
				on sto.StoreID=stf.StoreID
				inner join (
					select StoreID, StoreStaffID from tblStaff stf where (" + ParsePairSetOutput(ServerListCSV,"stf.StoreID","stf.StoreStaffID") + @")
					) srt
				on srt.StoreID=stf.StoreID
					and srt.StoreStaffID = stf.StoreStaffID");
			else		
				sb.Append(@"
				insert into @tblStaffServers
				select
					stf.StoreID*4294967296+stf.StoreStaffID as TableKey, stf.StoreID, stf.StoreStaffID, isnull(rtrim(stf.LastName)+', ','')+isnull(rtrim(stf.FirstName),'') as StaffName
				from
					tblStaff stf
				inner join
					@tblStores sto
				on sto.StoreID=stf.StoreID");
			sb.Append(@"
			set @CashierNameList = null
			if @CashierFlag=0
				if len(@CashierListCSV)=0
					set @CashierNameList = 'All'
				else
					begin
					declare @Cashier varchar(50)
					declare cur cursor for
						select distinct StoreStaff as Cashier
						from @tblStaffCashiers sc
						order by StoreStaff
					open cur
					if @@cursor_rows>8
						set @CashierDetailFlag = 1
					if @@cursor_rows>1
						set @CashierMultipleFlag = 1
					fetch next from cur into @Cashier
					while @@fetch_status=0
						begin
						if @CashierNameList is null
							set @CashierNameList = ''
						else
							if len(@CashierNameList)<995
								if @CashierDetailFlag=1
									set @CashierNameList = @CashierNameList + ', '
								else
									set @CashierNameList = @CashierNameList + '<br/>'
						set @CashierNameList = left(@CashierNameList + rtrim(isnull(@Cashier,'')),1000)
						fetch next from cur into @Cashier
						end
					close cur
					deallocate cur
					end
			set @ServerNameList = null
			if @ServerFlag=0
				if len(@ServerListCSV)=0
					set @ServerNameList = 'All'
				else
					begin
					declare @Server varchar(50)
					declare @ServerSort varchar(6)
					declare cur cursor for
						select distinct StoreStaff as Server
						from @tblStaffServers ss
						order by StoreStaff
					open cur
					if @@cursor_rows>8
						set @ServerDetailFlag = 1
					if @@cursor_rows>1
						set @ServerMultipleFlag = 1
					fetch next from cur into @Server
					while @@fetch_status=0
						begin
						if @ServerNameList is null
							set @ServerNameList = ''
						else
							if len(@ServerNameList)<995
								if @ServerDetailFlag=1
									set @ServerNameList = @ServerNameList + ', '
								else
									set @ServerNameList = @ServerNameList + '<br/>'
						set @ServerNameList = left(@ServerNameList + rtrim(isnull(@Server,'')),1000)
						fetch next from cur into @Server
						end
					close cur
					deallocate cur
					end
			set @CheckNumberNameList = null
			declare @CheckNumberID bigint
			declare @CheckNumber varchar(20)
			if len(@CheckNumberListCSV)=0
				begin
				set @CheckNumberNameList = 'All'
				set @CheckNumberMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select distinct
						ItemID as CheckNumberID,
						cast(floor(ItemID/cast(10000000000 as bigint)) as varchar(10))
							+ '-' + cast(floor((ItemID%cast(10000000000 as bigint))/100) as varchar(10))
							+ '-' + cast(ItemID%100 as varchar(9))
						as CheckNumber
					from
						dbo.fn_ParsePairSetBigIntDetail(@CheckNumberListCSV)
					order by ItemID
				open cur
				if @@cursor_rows>8
					set @CheckNumberDetailFlag = 1
				else
					if @@cursor_rows>1
						set @CheckNumberMultipleFlag=1
				fetch next from cur into @CheckNumberID, @CheckNumber
				while @@fetch_status=0
					begin
					if @CheckNumberNameList is null
						set @CheckNumberNameList = ''
					else
						if  len(@CheckNumberNameList)<995
							if @CheckNumberDetailFlag=1
								set @CheckNumberNameList = @CheckNumberNameList + ', '
							else
								set @CheckNumberNameList = @CheckNumberNameList + '<br/>'
					set @CheckNumberNameList = left(@CheckNumberNameList + rtrim(isnull(@CheckNumber,'')),1000)
					fetch next from cur into @CheckNumberID, @CheckNumber
					end
				close cur
				deallocate cur
				end
			declare @DayNumber int
			declare @DayName varchar(50)
			declare @DayCount int
			set @DayNameNameList = null
			if len(@DayNameListCSV)=0
				begin
				set @DayNameNameList = 'All'
				set @DayNameMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select distinct
						KeyValue as DayNumber,
						left(datename(mm,dbo.fn_CalcDayNumberDate(KeyValue)),3) + ' '
							+ datename(dd, dbo.fn_CalcDayNumberDate(KeyValue)) + ', '
							+ datename(yyyy, dbo.fn_CalcDayNumberDate(KeyValue))
						as DayName
					from
						fn_CreateKeyTableValueFromCSVString(@DayNameListCSV) csv
					order by KeyValue
				open cur
				if @@cursor_rows>8
					set @DayNameDetailFlag = 1
				else
					if @@cursor_rows>1
						set @DayNameMultipleFlag=1
				fetch next from cur into @DayNumber, @DayName
				while @@fetch_status=0
					begin
					if @DayNameNameList is null
						set @DayNameNameList = ''
					else
						if  len(@DayNameNameList)<895
							if @DayNameDetailFlag=1
								set @DayNameNameList = @DayNameNameList + ', '
							else
								set @DayNameNameList = @DayNameNameList + '<br/>'
					set @DayNameNameList = left(@DayNameNameList + rtrim(isnull(@DayName,'')),900)
					fetch next from cur into @DayNumber, @DayName
					end
				close cur
				deallocate cur
				end
			declare @WeekNumber int
			declare @WeekName varchar(50)
			declare @WeekCount int
			set @WeekNameNameList = null
			if len(@WeekNameListCSV)=0
				begin
				set @WeekNameNameList = 'All'
				set @WeekNameMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select distinct
						KeyValue as WeekNumber,
						left(datename(mm,dbo.fn_CalcDayNumberDate(KeyValue*7)),3) + ' '
							+ datename(dd, dbo.fn_CalcDayNumberDate(KeyValue*7)) + ', '
							+ datename(yyyy, dbo.fn_CalcDayNumberDate(KeyValue*7))
						as DayName
					from
						fn_CreateKeyTableValueFromCSVString(@WeekNameListCSV) csv
					order by KeyValue
				open cur
				if @@cursor_rows>8
					set @WeekNameDetailFlag = 1
				else
					if @@cursor_rows>1
						set @WeekNameMultipleFlag=1
				fetch next from cur into @WeekNumber, @WeekName
				while @@fetch_status=0
					begin
					if @WeekNameNameList is null
						set @WeekNameNameList = ''
					else
						if  len(@WeekNameNameList)<890
							if @WeekNameDetailFlag=1
								set @WeekNameNameList = @WeekNameNameList + ', '
							else
								set @WeekNameNameList = @WeekNameNameList + '<br/>'
					set @WeekNameNameList = left(@WeekNameNameList + rtrim(isnull(@WeekName,'')),895)
					fetch next from cur into @WeekNumber, @WeekName
					end
				close cur
				deallocate cur
				end
			insert into @table_variable (
				StoreGroupName, StoreName, ReceiptClass, ReceiptType, CreditCardBatchNumber, CashierStaffDepartmentName, CashierName, ServerName, ProfitCenter, CheckNumber, DayName, WeekName, CashierSessionNumber, DayHourNumber,
				StoreGroupDetailFlag, StoreDetailFlag, ReceiptClassDetailFlag, ReceiptTypeDetailFlag, CreditCardBatchNumberDetailFlag, CashierStaffDepartmentDetailFlag, CashierDetailFlag, ServerDetailFlag, ProfitCenterDetailFlag, CheckNumberDetailFlag, DayNameDetailFlag, WeekNameDetailFlag, CashierSessionNumberDetailFlag, DayHourNumberDetailFlag,
				StoreGroupMultipleFlag, StoreMultipleFlag, ReceiptClassMultipleFlag, ReceiptTypeMultipleFlag, CreditCardBatchNumberMultipleFlag, CashierStaffDepartmentMultipleFlag, CashierMultipleFlag, ServerMultipleFlag, ProfitCenterMultipleFlag, CheckNumberMultipleFlag, DayNameMultipleFlag, WeekNameMultipleFlag, CashierSessionNumberMultipleFlag, DayHourNumberMultipleFlag
				)
			values (
				@StoreGroupNameList, @StoreNameList, @ReceiptClassNameList, @ReceiptTypeNameList, @CreditCardBatchNumberNameList, @CashierStaffDepartmentName, @CashierNameList, @ServerNameList, @ProfitCenterNameList, @CheckNumberNameList, @DayNameNameList, @WeekNameNameList, @CashierSessionNumberNameList, @DayHourNumberListCSV,
				@StoreGroupDetailFlag, @StoreDetailFlag, @ReceiptClassDetailFlag, @ReceiptTypeDetailFlag, @CreditCardBatchNumberDetailFlag, @CashierStaffDepartmentDetailFlag, @CashierDetailFlag, @ServerDetailFlag, @ProfitCenterDetailFlag, @CheckNumberDetailFlag, @DayNameDetailFlag, @WeekNameDetailFlag, @CashierSessionNumberDetailFlag, @DayHourNumberDetailFlag,
				@StoreGroupMultipleFlag, @StoreMultipleFlag, @ReceiptClassMultipleFlag, @ReceiptTypeMultipleFlag, @CreditCardBatchNumberMultipleFlag, @CashierStaffDepartmentMultipleFlag, @CashierMultipleFlag, @ServerMultipleFlag, @ProfitCenterMultipleFlag, @CheckNumberMultipleFlag, @DayNameMultipleFlag, @WeekNameMultipleFlag, @CashierSessionNumberMultipleFlag, @DayHourNumberMultipleFlag
				)
			select * from @table_variable" + "\n");
			return sb.ToString();
			}

		public string ParsePairSetStringAddReceiptType(string strInputString, string strReceiptType) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddReceiptType('" + strInputString + "','" + FixSingleQuotesForSQL(strReceiptType) + "') as ReceiptTypeList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["ReceiptTypeList"];
			}

		public string ParsePairSetStringAddStaffDepartmentName(string strInputString, string strStaffDepartmentName) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddStaffDepartmentName('" + strInputString + "','" + FixSingleQuotesForSQL(strStaffDepartmentName) + "') as StaffDepartmentNameList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["StaffDepartmentNameList"];
			}

		public string ParsePairSetStringAddCheckNumber(string strInputString, string strCheckNumber) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddCheckNumber2('" + strInputString + "','" + FixSingleQuotesForSQL(strCheckNumber) + "') as CheckNumberList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["CheckNumberList"];
			}

		}
	}
