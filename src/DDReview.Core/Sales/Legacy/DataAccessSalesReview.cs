#nullable disable
using System;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Text;


namespace DDReview.Core.Sales.Legacy {

	public class DataAccessSalesReview : DataAccessReview {
		public Int32 StartDayNumber;
		public Int32 EndDayNumber;
		public Int32 OffsetDays;
		public Int32 DayPartFlag=0;
		public String DayPartListCSV="";
		public Int32 DayHourFlag=0;
		public String DayHourListCSV="";
		public Int32 DayMinuteFlag=0;
		public String DayMinuteListCSV="";
		public Int32 StoreGroupFlag=0;
		public String StoreGroupListCSV="";
		public Int32 StoreFlag=1;
		public String StoreListCSV="";
		public Int32 ProfitCenterFlag=0;
		public String ProfitCenterListCSV="";
		public Int32 MainTypeFlag=0;
		public String MainTypeListCSV="";
		public Int32 MainGroupFlag=0;
		public String MainGroupListCSV="";
		public Int32 SaleTypeFlag=0;
		public String SaleTypeListCSV="";
		public Int32 MenuItemFlag=0;
		public String MenuItemListCSV="";
		public Int32 MenuItemPLUFlag=0;
		public String MenuItemPLUListCSV="";
		/* if menu item modifier flag is 1,
		 *   then MenuItemPLU returns main menu item's PLU,
		 *     and modifier MenuItemModifierPLU will be the modifier's PLU from the current record.
		 *     and MenuItemModifierPLUListCSV will be a ThreeSet of CSV's StoreID:MenuItemPLU:MenuItemModifierPLU
		 *       where MenuItemModifierPLU is a composite PLU, of Period Separated Values (112.223.114.3345) that
		 *       taken together, becomes the menuitemmodifier name, and modifier names concatenated become
		 *       a secondary column, that also responds to clicks.  It also might work to have a separate control
		 *       definition in one column for handling this.  The Three
		 */
		public Int32 MenuItemModifierPLUFlag=0;
		public String MenuItemModifierPLUListCSV="";
		/* */
		public Int32 DiscountFlag=0;
		public String DiscountListCSV="";
		public Int32 SpecialFlag=0;
		public String SpecialListCSV="";
		public Int32 WeekFlag=0;
		public String WeekListCSV="";
		public Int32 WeekDayFlag=0;
		public String WeekDayListCSV="";
		public Int32 DayFlag=0;
		public String DayListCSV="";
		public Int32 FrequentDinerPlanFlag=0;
		public String FrequentDinerPlanListCSV="";
		public Int32 FrequentDinerCustomerFlag=0;
		public String FrequentDinerCustomerListCSV="";
		public Int32 StoreStaffFlag=0;
		public String StoreStaffListCSV="";
		public Int32 CheckNumberFlag=0;
		public String CheckNumberListCSV="";
		public Int32 CostAccountFlag=0;
		public String CostAccountListCSV="";
		public Int32 CostTrackingItemFlag=0;
		public String CostTrackingItemListCSV="";
		public Boolean IncludeDigitalDiningPlateCost = true;
		public Boolean IncludeVoids = true;
		public Boolean ColumnNumberExpressionRequiresDetailJoin = false;
		public enum ColumnOutputDisplayModes { WeekDay, MonthDay, DayHourNumber }
		public ColumnOutputDisplayModes ColumnOutputDisplayMode = ColumnOutputDisplayModes.WeekDay;
		public Int32 RowsReturned = 0;
		public Int32 CostMode = 0; /* 0 -> no cost, 1 -> menu file, 2 -> plate cost only, 3 -> current inventory cost, 4 -> historical inventory cost */
		public String PageClickStateXML = null;
		public String SQL = "";
		public String SQLHeader = "";
		public String DefaultSort = "";
		public String BaseSort = "";
		public Boolean FillDataSet = true;
		public Boolean ShowBatchSubmissionStatus = true;
		public Boolean RawExport = false;
		/// <summary>Reads the few small lookup tables the script depends on (day parts, schema probe). Swapped for a stub in tests.</summary>
		public ISalesLookups Lookups;

		private DateTime dtQueryStart;
		private DateTime dtQueryEnd;
		private TimeSpan tsQueryDuration;

		public DataAccessSalesReview(String ConnectionString, String PageClickStateXML) {
			this.ConnectionString = ConnectionString;
			this.PageClickStateXML = PageClickStateXML;
			}

		public DataAccessSalesReview(String ConnectionString) {
			this.ConnectionString = ConnectionString;
			}


		private void SalesSelect(StringBuilder sbParentSQLData, Int32 TabCount, Boolean SelectDetail, Boolean ShowBatchSubmissionStatus) {
			StringBuilder sbSQLData = new StringBuilder();
			sbSQLData.Append(@"
						select
							/* iq.SalesKey, */ iq.BatchID, iq.CompareIndex, iq.DayNumber, iq.StoreID, iq.ProfitCenterID,
							iq.MainTypeID, iq.MainGroupID, iq.SaleTypeID, iq.PLU, iq.Discount, iq.Special, iq.WeekNumber,
							iq.WeekDayNumber, iq.SaleItemID, iq.Amount, iq.Quantity, iq.ItemPrice,
							sum(DiscountAmount) as DiscountAmount,
							sum(DiscountQuantity) as DiscountQuantity,");
			if (ShowBatchSubmissionStatus)
				sbSQLData.Append(@"
								max(case when csb.BatchSubmissionStatus<4 then csb.BatchSubmissionStatus
									when datediff(mi,csb.PostingDate,csb.BatchSubmitted)<(1440+120) then 6
									when datediff(mi,csb.PostingDate,csb.BatchSubmitted)<(2880) then 5
									else 4
								end) as BatchSubmissionStatus,
							sum(RoyaltyExcludedDiscounts) as RoyaltyExcludedDiscounts");
			else
				sbSQLData.Append(@"
								0 as BatchSubmissionStatus,
							sum(RoyaltyExcludedDiscounts) as RoyaltyExcludedDiscounts");
			sbSQLData.Append(@"
						from (
							select
								--min(css.BatchID*2147483648+css.SaleItemID) as SalesKey,
								css.BatchID,
								case when css.DayNumber between @StartDayNumber and @EndDayNumber then 0 else 1 end as CompareIndex,
								css.DayNumber,
								" + strStoreIDExpression + @" as StoreID,
								" + (ProfitCenterFlag!=0?strProfitCenterIDExpression:"null") + @" as ProfitCenterID,
								css.MainTypeID,
								" + (MainGroupFlag!=0?"css.MainGroupID":"null") + @" as MainGroupID,
								" + (SaleTypeFlag!=0?"css.SaleTypeID":"null") + @" as SaleTypeID,
								" + (MenuItemFlag!=0||MenuItemPLUFlag!=0||CostMode>0?"css.PLU":"null") + @" as PLU,
								" + (DiscountFlag!=0||!blnCalculatedDiscounts?"css.Discount":"0 as Discount") + @",
								" + (SpecialFlag!=0?"css.Special":"null") + @" as Special,
								" + (WeekFlag!=0?"floor(css.DayNumber/7)":"null") + @" as WeekNumber,
								" + (WeekDayFlag!=0?"css.DayNumber%7":"null") + @" as WeekDayNumber,"
									+ ((CostMode==2)?"\n\t\t\t\t\t\tItemCost,":"") + @"
								" + (SelectDetail?"css.SaleItemID":"null as SaleItemID") + @",
								" + (DiscountListCSV.Length>0?"sum(case when css.Discount=0 then cssd.Amount else 0 end)":"sum(case when css.Discount=0 then css.Amount else 0 end)") + @" as Amount,
								" + (DiscountListCSV.Length>0?"sum(case when css.Discount=0 then cssd.Quantity else 0 end)":"sum(case when css.Discount=0 then css.Quantity else 0 end)") + @" as Quantity,
								max(case when css.Discount=0 then isnull(css.ItemPrice,0) else 0 end) as ItemPrice,
								sum(case when css.Discount<>0 then css.Amount else 0 end) as DiscountAmount,
								sum(case when css.Discount<>0 then css.Quantity else 0 end) as DiscountQuantity,
								0 as RoyaltyExcludedDiscounts
							from
								tblCashAndSalesSales css with (nolock)");
			if (DiscountListCSV.Length>0)
				sbSQLData.Append(@"
							inner join
								tblCashAndSalesSalesDetail cssd with (nolock)
							on cssd.BatchID=css.BatchID
								and cssd.SaleItemID=css.SaleItemID
							inner join (
								select distinct
									cssd.BatchID, cssd.CheckNumber, cssd.PersonNumber
								from
									tblCashAndSalesSales css with (nolock)
								inner join
									tblCashAndSalesSalesDetail cssd with (nolock)
								on css.StoreID=cssd.StoreID
									and css.SaleItemID=cssd.SaleItemID
								where ("+ParsePairSetOutput(DiscountListCSV.Replace(":",":0,"),"css.StoreID","css.Discount")+")" + @"
									and (css.DayNumber between @StartDayNumber and @EndDayNumber or css.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
									and (cssd.DayNumber between @StartDayNumber and @EndDayNumber or cssd.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
								) cq
							on cq.BatchID=cssd.BatchID
								and cq.CheckNumber=cssd.CheckNumber
								and cq.PersonNumber=cssd.PersonNumber");
			sbSQLData.Append(@"
							where");
			if (OffsetDays!=System.Int32.MinValue) {
				sbSQLData.Append(@"
								(css.DayNumber between @StartDayNumber and @EndDayNumber or
									css.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber) "
																	  + ((StoreListCSV.Length>0)?" and css.StoreID in (" + StoreListCSV + ")":""));
				if (DiscountListCSV.Length>0)
					sbSQLData.Append(@"
								and (cssd.DayNumber between @StartDayNumber and @EndDayNumber or
									cssd.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber) ");
				}
			else {
				sbSQLData.Append(@"
								css.DayNumber between @StartDayNumber and @EndDayNumber" + ((StoreListCSV.Length>0)?" and " + strStoreIDExpression + " in (" + StoreListCSV + ")":""));
				}
			sbSQLData.Append ("\t" + (strWhere.Length>0?"\t\t\t\t\t\t\t\tand " + strWhere :"") + "\n");
			sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.PLU=0 or css.PLU>=1000)" + "\n");
			if (ProfitCenterListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(ProfitCenterListCSV,"css.StoreID","css.ProfitCenterID") + ")\n");
			if (MainTypeListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.MainTypeID in (" + MainTypeListCSV + "))" + "\n");
			else
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.MainTypeID in (1,2,3) or @IncludeCharges<>0 and css.MainTypeID=4)" + "\n");
			if (MainGroupListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(MainGroupListCSV,"css.StoreID","css.MainGroupID") + ")\n");
			if (SaleTypeListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(SaleTypeListCSV,"css.StoreID","css.SaleTypeID") + ")\n");
			if (MenuItemListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(MenuItemListCSV,"css.StoreID","css.PLU") + ")\n");
			if (MenuItemPLUListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(MenuItemPLUListCSV,"css.StoreID","css.PLU") + ")\n");
			if (DiscountListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(DiscountListCSV.Replace(":",":0,"),"css.StoreID","css.Discount") + ")\n");
			if (SpecialListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.Special in (" + SpecialListCSV + "))" + "\n\n");
			if (WeekListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (floor(css.DayNumber/7) in (" + WeekListCSV + "))" + "\n");
			if (WeekDayListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand ((css.DayNumber%7) in (" + WeekDayListCSV + "))" + "\n");
			if (DayListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand ((css.DayNumber) in (" + DayListCSV + "))" + "\n");
			sbSQLData.Append(@"							group by
								css.BatchID,
								css.DayNumber,
								" + (!blnCalculatedDiscounts?"css.Discount,\n\t\t\t\t\t\t\t":"")
									+ strStoreIDExpression
							+ (ProfitCenterFlag!=0?",\n\t\t\t\t\t"+strProfitCenterIDExpression:"")
							+ ",\n\t\t\t\t\t\t\t\tcss.MainTypeID"
							+ (MainGroupFlag!=0?",\n\t\t\t\t\t\t\tcss.MainGroupID":"")
							+ (SaleTypeFlag!=0?",\n\t\t\t\t\t\t\tcss.SaleTypeID":"")
							+ (MenuItemFlag!=0||MenuItemPLUFlag!=0||CostMode>0?",\n\t\t\t\t\t\t\tcss.PLU":"")
							+ (DiscountFlag!=0?",\n\t\t\t\t\t\t\tcss.Discount":"")
							+ (SpecialFlag!=0?",\n\t\t\t\t\t\t\tcss.Special":"")
							+ (WeekFlag!=0?",\n\t\t\t\t\t\t\tfloor(css.DayNumber/7)":"")
							+ (WeekDayFlag!=0?",\n\t\t\t\t\t\t\tcss.DayNumber%7":"")
							+ ((CostMode==2)?",\n\t\t\t\t\t\t\t\tItemCost":"")
							+ (SelectDetail?",\n\t\t\t\t\t\t\t\tcss.SaleItemID":"") + @"
							union all
							select
								--min(css.BatchID*2147483648+css.SaleItemID) as SalesKey,
								css.BatchID,
								case when css.DayNumber between @StartDayNumber and @EndDayNumber then 0 else 1 end as CompareIndex,
								css.DayNumber,
								" + strStoreIDExpression + @" as StoreID,
								" + (ProfitCenterFlag!=0?strProfitCenterIDExpression:"null") + @" as ProfitCenterID,
								css.MainTypeID,
								" + (MainGroupFlag!=0?"css.MainGroupID":"null") + @" as MainGroupID,
								" + (SaleTypeFlag!=0?"css.SaleTypeID":"null") + @" as SaleTypeID,
								" + (MenuItemFlag!=0||MenuItemPLUFlag!=0||CostMode>0?"css.PLU":"null") + @" as PLU,
								" + (DiscountFlag!=0||!blnCalculatedDiscounts?"css.Discount":"0 as Discount") + @",
								" + (SpecialFlag!=0?"css.Special":"null") + @" as Special,
								" + (WeekFlag!=0?"floor(css.DayNumber/7)":"null") + @" as WeekNumber,
								" + (WeekDayFlag!=0?"css.DayNumber%7":"null") + @" as WeekDayNumber,"
									+ ((CostMode==2)?"\n\t\t\t\t\t\tItemCost,":"") + @"
								" + (SelectDetail?"css.SaleItemID":"null as SaleItemID") + @",
								0 as Amount,
								0 as Quantity,
								0 as ItemPrice,
								0 as DiscountAmount,
								0 as DiscountQuantity,
								sum(case when css.Discount<>0 then css.Amount else 0 end) as RoyaltyExcludedDiscounts
							from
								tblCashAndSalesSales css with (nolock) inner join tblDiscounts dsc with (nolock) on dsc.StoreID=css.StoreID and dsc.DiscountID=css.Discount ");
			if (DiscountListCSV.Length>0)
				sbSQLData.Append(@"
							inner join
								tblCashAndSalesSalesDetail cssd with (nolock)
							on cssd.BatchID=css.BatchID
								and cssd.SaleItemID=css.SaleItemID
							inner join (
								select distinct
									cssd.BatchID, cssd.CheckNumber, cssd.PersonNumber
								from
									tblCashAndSalesSales css with (nolock)
								inner join
									tblCashAndSalesSalesDetail cssd with (nolock)
								on css.StoreID=cssd.StoreID
									and css.SaleItemID=cssd.SaleItemID
								where ("+ParsePairSetOutput(DiscountListCSV.Replace(":",":0,"),"css.StoreID","css.Discount")+")" + @"
									and (css.DayNumber between @StartDayNumber and @EndDayNumber or css.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
									and (cssd.DayNumber between @StartDayNumber and @EndDayNumber or cssd.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
								) cq
							on cq.BatchID=cssd.BatchID
								and cq.CheckNumber=cssd.CheckNumber
								and cq.PersonNumber=cssd.PersonNumber");
			sbSQLData.Append(@"
							where");
			if (OffsetDays!=System.Int32.MinValue) {
				sbSQLData.Append(@"
								(css.DayNumber between @StartDayNumber and @EndDayNumber or
									css.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber) "
																	  + ((StoreListCSV.Length>0)?" and css.StoreID in (" + StoreListCSV + ")":""));
				if (DiscountListCSV.Length>0)
					sbSQLData.Append(@"
								and (cssd.DayNumber between @StartDayNumber and @EndDayNumber or
									cssd.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber) ");
				}
			else {
				sbSQLData.Append(@"
								css.DayNumber between @StartDayNumber and @EndDayNumber" + ((StoreListCSV.Length>0)?" and " + strStoreIDExpression + " in (" + StoreListCSV + ")":""));
				}
			sbSQLData.Append ("\t" + (strWhere.Length>0?"\t\t\t\t\t\t\t\tand " + strWhere :"") + "\n");
			sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.PLU=0 or css.PLU>=1000) and css.Discount<>0 and dsc.DiscountDoesNotReduceRoyalty<>0\n");
			if (ProfitCenterListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(ProfitCenterListCSV,"css.StoreID","css.ProfitCenterID") + ")\n");
			if (MainTypeListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.MainTypeID in (" + MainTypeListCSV + "))" + "\n");
			else
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.MainTypeID in (1,2,3) or @IncludeCharges<>0 and css.MainTypeID=4)" + "\n");
			if (MainGroupListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(MainGroupListCSV,"css.StoreID","css.MainGroupID") + ")\n");
			if (SaleTypeListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(SaleTypeListCSV,"css.StoreID","css.SaleTypeID") + ")\n");
			if (MenuItemListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(MenuItemListCSV,"css.StoreID","css.PLU") + ")\n");
			if (MenuItemPLUListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(MenuItemPLUListCSV,"css.StoreID","css.PLU") + ")\n");
			if (DiscountListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(DiscountListCSV.Replace(":",":0,"),"css.StoreID","css.Discount") + ")\n");
			if (SpecialListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (css.Special in (" + SpecialListCSV + "))" + "\n\n");
			if (WeekListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand (floor(css.DayNumber/7) in (" + WeekListCSV + "))" + "\n");
			if (WeekDayListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand ((css.DayNumber%7) in (" + WeekDayListCSV + "))" + "\n");
			if (DayListCSV.Length>0)
				sbSQLData.Append ("\t\t\t\t\t\t\t\t\tand ((css.DayNumber) in (" + DayListCSV + "))" + "\n");
			sbSQLData.Append(@"							group by
								css.BatchID,
								css.DayNumber,
								" + (!blnCalculatedDiscounts?"css.Discount,\n\t\t\t\t\t\t\t":"")
									+ strStoreIDExpression
							+ (ProfitCenterFlag!=0?",\n\t\t\t\t\t"+strProfitCenterIDExpression:"")
							+ ",\n\t\t\t\t\t\t\t\tcss.MainTypeID"
							+ (MainGroupFlag!=0?",\n\t\t\t\t\t\t\tcss.MainGroupID":"")
							+ (SaleTypeFlag!=0?",\n\t\t\t\t\t\t\tcss.SaleTypeID":"")
							+ (MenuItemFlag!=0||MenuItemPLUFlag!=0||CostMode>0?",\n\t\t\t\t\t\t\tcss.PLU":"")
							+ (DiscountFlag!=0?",\n\t\t\t\t\t\t\tcss.Discount":"")
							+ (SpecialFlag!=0?",\n\t\t\t\t\t\t\tcss.Special":"")
							+ (WeekFlag!=0?",\n\t\t\t\t\t\t\tfloor(css.DayNumber/7)":"")
							+ (WeekDayFlag!=0?",\n\t\t\t\t\t\t\tcss.DayNumber%7":"")
							+ ((CostMode==2)?",\n\t\t\t\t\t\t\t\tItemCost":"")
							+ (SelectDetail?",\n\t\t\t\t\t\t\t\tcss.SaleItemID":"") + @"

							) iq");
			if (ShowBatchSubmissionStatus)
				sbSQLData.Append(@"
						inner join
							tblCashAndSalesBatches csb
						on csb.BatchID=iq.BatchID
						where csb.DayNumber between @StartDayNumber and @EndDayNumber
							or csb.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber");
			sbSQLData.Append(@"
						group by
							/* iq.SalesKey, */ iq.BatchID, iq.CompareIndex, iq.DayNumber, iq.StoreID, iq.ProfitCenterID,
							iq.MainTypeID, iq.MainGroupID, iq.SaleTypeID, iq.PLU, iq.Discount, iq.Special, iq.WeekNumber,
							iq.WeekDayNumber, iq.SaleItemID, iq.Amount, iq.Quantity, iq.ItemPrice");
			sbParentSQLData.Append(sbSQLData.ToString().Replace("\n\t\t\t\t\t\t","\n" + new String('\t',TabCount)));
			}
		private void SalesDetailSelect(StringBuilder sbParentSQLData, Int32 TabCount) {
			StringBuilder sbSQLData = new StringBuilder();
			sbSQLData.Append(@"
							select
								--cssdx.BatchID*2147483648+SaleItemID as SalesKey,
								cssdx.BatchID,
								cssdx.SaleItemID");
			if (DayPartFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + strDayPartExpression + " as DayPartID");
			else
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "null as DayPartID");
			if (DayHourFlag==1 || ColumnOutputDisplayMode==ColumnOutputDisplayModes.DayHourNumber)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.DayHourNumber");
			else
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "null as DayHourNumber");
			if (FrequentDinerPlanFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.FrequentDinerPlanID");
			else
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "null as FrequentDinerPlanID");
			if (FrequentDinerCustomerFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.FrequentDinerCustomerID");
			else
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "null as FrequentDinerCustomerID");
			if (StoreStaffFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.StoreStaffID");
			else
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "null as StoreStaffID");
			if (CheckNumberFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cast(cssdx.StoreID as bigint)*10000000000+cssdx.CheckNumber*100+cssdx.PersonNumber as CheckNumberID");
			else
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "null as CheckNumberID");
			sbSQLData.Append(@",
								sum(case when cssdx.SaleItemID>=0 then cssdx.Amount else 0 end) as Amount,
								sum(case when cssdx.SaleItemID>=0 then cssdx.Quantity else 0 end) as Quantity,
								sum(case when cssdx.SaleItemID<0 then cssdx.Amount else 0 end) as DiscountAmount,
								sum(case when cssdx.SaleItemID<0 then cssdx.Quantity else 0 end) as DiscountQuantity,
								sum(case when cssdx.SaleItemID<0 and isnull(cssdx.MainItemID,0)&1<>0 then cssdx.Amount else 0 end) as RoyaltyExcludedDiscounts
							from 
								tblCashAndSalesSalesDetail cssdx with (nolock)");
			if (DiscountListCSV.Length>0) {
				sbSQLData.Append(@"
							inner join (
								select distinct
									cssd.BatchID, cssd.CheckNumber, cssd.PersonNumber
								from
									tblCashAndSalesSales css with (nolock)
								inner join
									tblCashAndSalesSalesDetail cssd with (nolock)
								on css.StoreID=cssd.StoreID
									and css.SaleItemID=cssd.SaleItemID
								where ("+ParsePairSetOutput(DiscountListCSV.Replace(":",":0,"),"css.StoreID","css.Discount")+")" + @"
									and (css.DayNumber between @StartDayNumber and @EndDayNumber or css.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
									and (cssd.DayNumber between @StartDayNumber and @EndDayNumber or cssd.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
							) cq
							on cq.BatchID=cssdx.BatchID
								and cq.CheckNumber=cssdx.CheckNumber
								and cq.PersonNumber=cssdx.PersonNumber");
					}
			sbSQLData.Append(@"
							where (
								cssdx.DayNumber between @StartDayNumber and @EndDayNumber
								or cssdx.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)" + ((StoreListCSV.Length>0)?" and cssdx.StoreID in (" + StoreListCSV + ")":""));
			if (DiscountListCSV.Length>0)
				sbSQLData.Append(@"");
			if (DayPartListCSV.Length>0)
				sbSQLData.Append("\n\t\t\t\t\t\t\t\tand (" + strDayPartExpression + @" in (" + DayPartListCSV + "))");
			if (DayHourListCSV.Length>0)
				sbSQLData.Append("\n\t\t\t\t\t\t\t\tand (cssdx.DayHourNumber in (" + DayHourListCSV + "))");
			if (FrequentDinerPlanListCSV.Length>0)
				sbSQLData.Append ("\n\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(FrequentDinerPlanListCSV,"cssdx.StoreID","cssdx.FrequentDinerPlanID") + ")");
			if (FrequentDinerCustomerListCSV.Length>0)
				sbSQLData.Append ("\n\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(FrequentDinerCustomerListCSV,"cssdx.StoreID","cssdx.FrequentDinerCustomerID") + ")");
			if (StoreStaffListCSV.Length>0)
				sbSQLData.Append ("\n\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(StoreStaffListCSV,"cssdx.StoreID","cssdx.StoreStaffID") + ")");
			if (CheckNumberListCSV.Length>0)
				sbSQLData.Append ("\n\t\t\t\t\t\t\t\t\tand (" + ParsePairSetOutput(CheckNumberListCSV,"cssdx.StoreID","cast(cssdx.StoreID as bigint)*10000000000+cssdx.CheckNumber*100+cssdx.PersonNumber") + ")");
			sbSQLData.Append (@"
							group by
								cssdx.BatchID,
								cssdx.SaleItemID");
			if (DayPartFlag==1 && strDayPartExpression!="1")
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + strDayPartExpression);
			if (DayHourFlag==1 || ColumnOutputDisplayMode==ColumnOutputDisplayModes.DayHourNumber )
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.DayHourNumber");
			if (FrequentDinerPlanFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.FrequentDinerPlanID");
			if (FrequentDinerCustomerFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.FrequentDinerCustomerID");
			if (StoreStaffFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cssdx.StoreStaffID");
			if (CheckNumberFlag==1)
				sbSQLData.Append(",\n\t\t\t\t\t\t\t\t" + "cast(cssdx.StoreID as bigint)*10000000000+cssdx.CheckNumber*100+cssdx.PersonNumber");
			sbParentSQLData.Append(sbSQLData.ToString().Replace("\n\t\t\t\t\t\t\t","\n" + new String('\t',TabCount)));
			}
		private String strStoreIDExpression = "";
		private String strStoreIDExpressionCore = "";
		private String strProfitCenterIDExpression = "";
		private String strProfitCenterIDExpressionCore = "";
		private String strStoreWhere = "";
		private Boolean blnCalculatedDiscounts = false;
		private String strWhere = "";
		String strDayPartExpression = "case when cssdx.TimeInMinutes >=360 and cssdx.TimeInMinutes <600 then 1 when cssdx.TimeInMinutes >=600 and cssdx.TimeInMinutes <960 then 2 when cssdx.TimeInMinutes >=960 and cssdx.TimeInMinutes <1440 then 3 when cssdx.TimeInMinutes >=0 and cssdx.TimeInMinutes <360 then 3 end";
		public String BuildDataScript(Boolean isMarauder) { return strSQLData(0, isMarauder); }
		public String BuildHeaderScript(String storeListAuthorizedSales) { return strSQLHeader(storeListAuthorizedSales); }
		public String SQLDataSelectStatement(Int32 WebLoginID, Boolean IsMarauder) {
			return strSQLData(WebLoginID,IsMarauder);
			}
		private String strSQLData (Int32 WebLoginID, Boolean IsMarauder) {
			String ColumnNumberExpression;
			Boolean TheoreticalCostPercentageFieldExists = true;
			IncludeVoids = 
			 (StoreGroupFlag==1 || StoreFlag==1) &&
				WeekFlag==0 &&
				WeekDayFlag==0 &&
				DayFlag==0 &&
				DayPartFlag==0 &&
				DayHourFlag==0 &&
				ProfitCenterFlag==0 &&
				MainTypeFlag==0 &&
				MainGroupFlag==0 &&
				SaleTypeFlag==0 &&
				MenuItemFlag==0 &&
				MenuItemPLUFlag==0 &&
				DiscountFlag==0 &&
				SpecialFlag==0 &&
				FrequentDinerPlanFlag==0 &&
				FrequentDinerCustomerFlag==0 &&
				StoreStaffFlag==0 &&
				CheckNumberFlag==0 &&
				CostAccountFlag==0 &&
				CostTrackingItemFlag==0;
			if (this.ColumnOutputDisplayMode==ColumnOutputDisplayModes.DayHourNumber) {
				ColumnNumberExpression = "css.DayHourNumber"; // "case when css.DayHourNumber<20 then 1 when css.DayHourNumber<25 then css.DayHourNumber-18 else 7 end";
				ColumnNumberExpressionRequiresDetailJoin = false;
				}
			else { // (this.ColumnOutputDisplayMode==ColumnOutputDisplayModes.Weekday)
				ColumnNumberExpression = "css.DayNumber%7+1";
				ColumnNumberExpressionRequiresDetailJoin = false;
				}
			String strSQL = "select StartTime, EndTime, DayPartID, DayPart from dbo.fn_DayPartsTable(1) dpt order by DayPartID, Visible desc";
			String strSQL2 = "select top 0 * from tblCashAndSalesBatches";
			DataSet ds = new DataSet();
			ds.Tables.Add(Lookups.GetTable("DayPartTable", strSQL));
			ds.Tables.Add(Lookups.GetTable("tblCashAndSalesBatches", strSQL2));
			blnCalculatedDiscounts = ds.Tables["tblCashAndSalesBatches"].Columns.Contains("DiscountsCalculated");
			TheoreticalCostPercentageFieldExists = true;
			DataView dv = new DataView(ds.Tables["DayPartTable"]);
			if (dv.Count>0) {
				StringBuilder sb = new StringBuilder("case ", 1000);
				for (int ii=0;ii<dv.Count;ii++) {
					sb.Append ("when cssdx.TimeInMinutes>=" + ((System.Int16)dv[ii]["StartTime"]).ToString() + " and cssdx.TimeInMinutes<" + ((System.Int16)dv[ii]["EndTime"]).ToString() + " then " + ((System.Int16)dv[ii]["DayPartID"]).ToString() + " ");
					}
				sb.Append ("end");
				strDayPartExpression = sb.ToString();
				}
			else
				strDayPartExpression = "1";
			strStoreIDExpression = "css.StoreID";
			strProfitCenterIDExpression = "css.ProfitCenterID";
			if (!IsMarauder) {
				strSQL = @"
					select
						tblProfitCenters.StoreID as SourceStoreID,
						tblProfitCenters.ProfitCenterID as SourceProfitCenterID,
						tblStoresPseudoProfitCenters.StoreID as DestinationStoreID,
						tblStoresPseudoProfitCenters.ProfitCenterItemID as DestinationProfitCenterItemID
					from
						tblStores
					inner join
						tblStoresPseudoProfitCenters
					on tblStores.StoreID=tblStoresPseudoProfitCenters.StoreID
					inner join
						tblProfitCenters
					on tblProfitCenters.ItemID=tblStoresPseudoProfitCenters.ProfitCenterItemID"
					+ "\n\t\t\t\t";
				ds = new DataSet();
				ds.Tables.Add(Lookups.GetTable("ProfitCenterSubstitutionTable", strSQL));
				dv = new DataView(ds.Tables["ProfitCenterSubstitutionTable"]);
				if (StoreListCSV.Length>0)
					strStoreWhere = "StoreID in (" + StoreListCSV.ToString() + ")";
				if (dv.Count>0) {
					strStoreIDExpressionCore = "";
					strProfitCenterIDExpressionCore = "";
					for (int ii=0;ii<dv.Count;ii++) {
						strStoreIDExpressionCore += "when css.StoreID=" + ((int)dv[ii]["SourceStoreID"]).ToString()
							+ " and css.ProfitCenterID=" + ((int)dv[ii]["SourceProfitCenterID"]).ToString() + " then "
							+ ((int)dv[ii]["DestinationStoreID"]).ToString();
						strProfitCenterIDExpressionCore += "when css.StoreID=" + ((int)dv[ii]["SourceStoreID"]).ToString()
							+ " and css.ProfitCenterID=" + ((int)dv[ii]["SourceProfitCenterID"]).ToString() + " then "
							+ ((int)dv[ii]["DestinationProfitCenterItemID"]).ToString();
						if (strStoreWhere.Length>0)
							strStoreWhere += " or ";
						strStoreWhere += "css.StoreID=" +((int)dv[ii]["SourceStoreID"]).ToString() + " and css.ProfitCenterID=" + ((int)dv[ii]["SourceProfitCenterID"]).ToString();
						}
					strStoreIDExpression = "case " + strStoreIDExpressionCore +" else css.StoreID end";
					strProfitCenterIDExpression = "case " + strProfitCenterIDExpressionCore +" else css.ProfitCenterID end";
					}
				strSQL = @"
					select distinct
						tblProfitCenters.StoreID as SourceStoreID,
						tblStoresPseudoProfitCenters.StoreID as DestinationStoreID
					from
						tblStoresPseudoProfitCenters
					inner join
						tblProfitCenters
					on tblProfitCenters.ItemID=tblStoresPseudoProfitCenters.ProfitCenterItemID"
					+ (StoreListCSV.Length>0?"\n\t\t\t\twhere tblStoresPseudoProfitCenters.StoreID in (" + StoreListCSV.ToString() + ")":"");
				ds = new DataSet();
				ds.Tables.Add(Lookups.GetTable("PseudoStoreSubstitutionTable", strSQL));
				dv = new DataView(ds.Tables["PseudoStoreSubstitutionTable"]);
				}
			String strMainGroupJoin = "";
			String strSaleTypeJoin = "";
			String strMenuItemJoin = "";
			String strDiscountJoin = "";
			System.Text.StringBuilder sbSQLData = new System.Text.StringBuilder();
			sbSQLData.Append (@"
			declare @StartDayNumber int
			declare @EndDayNumber int
			declare @CompareStartDayNumber int
			declare @CompareEndDayNumber int
			declare @DayPartFlag int
			declare @DayPartListCSV varchar(8000)
			declare @DayHourFlag int
			declare @DayHourListCSV varchar(8000)
			declare @StoreGroupFlag bit
			declare @StoreGroupListCSV varchar(8000)
			declare @StoreFlag bit
			declare @StoreListCSV varchar(8000)
			declare @ProfitCenterFlag bit
			declare @ProfitCenterListCSV varchar(8000)
			declare @MainTypeFlag bit
			declare @MainTypeListCSV varchar(8000)
			declare @MainGroupFlag bit
			declare @MainGroupListCSV varchar(8000)
			declare @SaleTypeFlag bit
			declare @SaleTypeListCSV varchar(8000)
			declare @MenuItemFlag bit
			declare @MenuItemListCSV varchar(8000)
			declare @MenuItemPLUFlag bit
			declare @MenuItemPLUListCSV varchar(8000)
			declare @DiscountFlag bit
			declare @DiscountListCSV varchar(8000)
			declare @SpecialFlag bit
			declare @SpecialListCSV varchar(8000)
			declare @WeekFlag bit
			declare @WeekListCSV varchar(8000)
			declare @WeekDayFlag bit
			declare @WeekDayListCSV varchar(8000)
			declare @DayFlag bit
			declare @DayListCSV varchar(8000)
			declare @FrequentDinerPlanFlag bit
			declare @FrequentDinerPlanListCSV varchar(8000)
			declare @FrequentDinerCustomerFlag bit
			declare @FrequentDinerCustomerListCSV varchar(8000)
			declare @StoreStaffFlag bit
			declare @StoreStaffListCSV varchar(8000)
			declare @CheckNumberFlag bit
			declare @CheckNumberListCSV varchar(8000)
			declare @CostAccountFlag bit
			declare @CostAccountListCSV varchar(8000)
			declare @CostTrackingItemFlag bit
			declare @CostTrackingItemListCSV varchar(8000)
			declare @IncludeVoids bit" + "\n");
			ShowBatchSubmissionStatus = (StoreGroupFlag==0 &&
				DayPartFlag==0 && DayHourFlag==0 && MainTypeFlag==0 && MainGroupFlag==0
					&& SaleTypeFlag==0 && MenuItemFlag==0 && MenuItemModifierPLUFlag==0
					&& DiscountFlag==0 && FrequentDinerPlanFlag==0 && FrequentDinerCustomerFlag==0
					&& StoreStaffFlag==0 && CheckNumberFlag==0 && CostAccountFlag==0 && CostTrackingItemFlag==0);
			sbSQLData.Append (
				@"			set @IncludeVoids=" + (IncludeVoids?"1":"0") +"\n"
				+ @"			set @StartDayNumber=" + StartDayNumber.ToString()+"\n"
				+ @"			set @EndDayNumber=" + EndDayNumber.ToString()+"\n"
				+ @"			set @CompareStartDayNumber=" + (StartDayNumber+OffsetDays).ToString()+"\n"
				+ @"			set @CompareEndDayNumber=" + (EndDayNumber+OffsetDays).ToString()+"\n"
				+ @"			set @DayPartFlag=" +DayPartFlag.ToString()+"\n"
				+ @"			set @DayPartListCSV='" + DayPartListCSV.ToString()+"'\n"
				+ @"			set @DayHourFlag=" +DayHourFlag.ToString()+"\n"
				+ @"			set @DayHourListCSV='" + DayHourListCSV.ToString()+"'\n"
				+ @"			set @StoreGroupFlag=" + StoreGroupFlag.ToString()+"\n"
				+ @"			set @StoreGroupListCSV='" + StoreGroupListCSV.ToString()+"'\n"
				+ @"			set @StoreFlag=" + StoreFlag.ToString()+"\n"
				+ @"			set @StoreListCSV='" + StoreListCSV.ToString()+"'\n"
				+ @"			set @ProfitCenterFlag=" + ProfitCenterFlag.ToString()+"\n"
				+ @"			set @ProfitCenterListCSV='" + ProfitCenterListCSV.ToString()+"'\n"
				+ @"			set @MainTypeFlag=" + MainTypeFlag.ToString()+"\n"
				+ @"			set @MainTypeListCSV='" + MainTypeListCSV.ToString()+"'\n"
				+ @"			set @MainGroupFlag=" + MainGroupFlag.ToString()+"\n"
				+ @"			set @MainGroupListCSV='" + MainGroupListCSV.ToString()+"'\n"
				+ @"			set @SaleTypeFlag=" + SaleTypeFlag.ToString()+"\n"
				+ @"			set @SaleTypeListCSV='" + SaleTypeListCSV.ToString()+"'\n"
				+ @"			set @MenuItemFlag=" + MenuItemFlag.ToString()+"\n"
				+ @"			set @MenuItemListCSV='" + MenuItemListCSV.ToString()+"'\n"
				+ @"			set @MenuItemPLUFlag=" + MenuItemPLUFlag.ToString()+"\n"
				+ @"			set @MenuItemPLUListCSV='" + MenuItemPLUListCSV.ToString()+"'\n"
				+ @"			set @DiscountFlag=" + DiscountFlag.ToString()+"\n"
				+ @"			set @DiscountListCSV='" + DiscountListCSV.ToString()+"'\n"
				+ @"			set @SpecialFlag=" + SpecialFlag.ToString()+"\n"
				+ @"			set @SpecialListCSV='" + SpecialListCSV.ToString()+"'\n"
				+ @"			set @WeekFlag=" + WeekFlag.ToString()+"\n"
				+ @"			set @WeekListCSV='" + WeekListCSV.ToString()+"'\n"
				+ @"			set @WeekDayFlag=" + WeekDayFlag.ToString()+"\n"
				+ @"			set @WeekDayListCSV='" + WeekDayListCSV.ToString()+"'\n"
				+ @"			set @DayFlag=" + DayFlag.ToString()+"\n"
				+ @"			set @DayListCSV='" + DayListCSV.ToString()+"'\n"
				+ @"			set @FrequentDinerPlanFlag=" + FrequentDinerPlanFlag.ToString()+"\n"
				+ @"			set @FrequentDinerPlanListCSV='" + FrequentDinerPlanListCSV.ToString()+"'\n"
				+ @"			set @FrequentDinerCustomerFlag=" + FrequentDinerCustomerFlag.ToString()+"\n"
				+ @"			set @FrequentDinerCustomerListCSV='" + FrequentDinerCustomerListCSV.ToString()+"'\n"
				+ @"			set @StoreStaffFlag=" + StoreStaffFlag.ToString()+"\n"
				+ @"			set @StoreStaffListCSV='" + StoreStaffListCSV.ToString()+"'\n"
				+ @"			set @CheckNumberFlag=" + CheckNumberFlag.ToString()+"\n"
				+ @"			set @CheckNumberListCSV='" + CheckNumberListCSV.ToString()+"'\n"
				+ @"			set @CostAccountFlag=" + CostAccountFlag.ToString()+"\n"
				+ @"			set @CostAccountListCSV='" + CostAccountListCSV.ToString()+"'\n"
				+ @"			set @CostTrackingItemFlag=" + CostTrackingItemFlag.ToString()+"\n"
				+ @"			set @CostTrackingItemListCSV='" + CostTrackingItemListCSV.ToString()+"'\n");
			sbSQLData.Append(@"
				declare @MenuMixPercentOfTotalFactor decimal(18,12)
				set @MenuMixPercentOfTotalFactor = 0.7" + "\n");
			sbSQLData.Append (@"
				declare @DayPartsTable table (
					StartTime smallint,
					EndTime smallint,
					DayPartID int,
					DayPart varchar(50)
					)
				declare @IncludeCharges bit
				declare @ic int
				set @IncludeCharges = 0
				set @ic = dbo.fn_GlobalSettingsGetInt('DDReview', 'IncludeChargesOnSalesReviewNew')
				if not @ic is null
					set @IncludeCharges = case when @ic<>0 then 1 else 0 end
				insert into @DayPartsTable
					select StartTime, EndTime, DayPartID, DayPart from dbo.fn_DayPartsTable(0) dpt
				declare @DayHourTable table (DayHourNumber smallint, DayHourName varchar(50))
				if len(@DayHourListCSV)>0
					insert into @DayHourTable
						select
							KeyValue as DayHourNumber,
							case
								when KeyValue in (0,24)
									then 'Midnight'
								when KeyValue in (12,36)
									then 'Noon'
								else right('  ' + cast(KeyValue as varchar(3)),2) +
									case when floor(KeyValue/12)%2=1 then ':00 PM' else ':00 AM' end
							end as DayHourName
						from
							fn_CreateKeyTableValueFromCSVString(@DayHourListCSV) csv
				else
					begin
					declare @KeyValue smallint
					set @KeyValue = 0
					while @KeyValue<48
						begin
						insert into @DayHourTable (DayHourNumber, DayHourName) values (
							@KeyValue,
							case
								when @KeyValue in (0,24)
									then 'Midnight'
								when @KeyValue in (12,36)
									then 'Noon'
								else right('  ' + cast(@KeyValue as varchar(3)),2) +
									case when floor(@KeyValue/12)%2=1 then ':00 PM' else ':00 AM' end
							end
							)
						set @KeyValue = @KeyValue + 1
						end
					end
				declare @BaseDate datetime
				declare @BaseDayNumber int
				set @BaseDate = dbo.fn_CalcWeekNumberBaseDate()
				set @BaseDayNumber = dbo.fn_CalcDayNumber(@BaseDate)
				declare @tblWeekDays table (WeekDayNumber int, WeekDayName varchar(16))
				if len(@WeekDayListCSV)>0
					begin
						insert into @tblWeekDays
							select
								csv.KeyValue as WeekDayNumber,
								WeekDayName
							from (
								select 0 as KeyValue, datename(dw,dateadd(dd,0,@BaseDate)) as WeekDayName
								union all select 1 as KeyValue, datename(dw,dateadd(dd,1,@BaseDate)) as WeekDayName
								union all select 2 as KeyValue, datename(dw,dateadd(dd,2,@BaseDate)) as WeekDayName
								union all select 3 as KeyValue, datename(dw,dateadd(dd,3,@BaseDate)) as WeekDayName
								union all select 4 as KeyValue, datename(dw,dateadd(dd,4,@BaseDate)) as WeekDayName
								union all select 5 as KeyValue, datename(dw,dateadd(dd,5,@BaseDate)) as WeekDayName
								union all select 6 as KeyValue, datename(dw,dateadd(dd,6,@BaseDate)) as WeekDayName
								) iq
							inner join
								fn_CreateKeyTableValueFromCSVString(@WeekDayListCSV) csv
							on csv.KeyValue=iq.KeyValue
					end
				else 
					begin
					insert into @tblWeekDays
						select
							KeyValue as WeekDayNumber,
							WeekDayName
						from (
							select 0 as KeyValue, datename(dw,dateadd(dd,0,@BaseDate)) as WeekDayName
							union all select 1 as KeyValue, datename(dw,dateadd(dd,1,@BaseDate)) as WeekDayName
							union all select 2 as KeyValue, datename(dw,dateadd(dd,2,@BaseDate)) as WeekDayName
							union all select 3 as KeyValue, datename(dw,dateadd(dd,3,@BaseDate)) as WeekDayName
							union all select 4 as KeyValue, datename(dw,dateadd(dd,4,@BaseDate)) as WeekDayName
							union all select 5 as KeyValue, datename(dw,dateadd(dd,5,@BaseDate)) as WeekDayName
							union all select 6 as KeyValue, datename(dw,dateadd(dd,6,@BaseDate)) as WeekDayName
							) iq
					end
				declare @temp_table table (
					CompareIndex smallint,
					DayPartID int,
					DayPart varchar(50),
					DayPartSort varchar(6),
					DayHourNumber int,
					DayHourName varchar(50),
					FrequentDinerPlanID int,
					FrequentDinerPlan varchar(30),
					FrequentDinerCustomerID int,
					FrequentDinerCustomer varchar(50),
					StoreStaffID int,
					StoreStaff varchar(102),
					CheckNumberID bigint,
					CheckNumber varchar(20),
					StoreGroupID int,
					StoreGroup varchar(50),
					StoreGroupSort varchar(6),
					StoreID int,
					StoreNumber int,
					StoreName varchar(50),
					StoreGCode varchar(30),
					ProfitCenterID int,
					ProfitCenter varchar(50),
					MainTypeID int, 
					MainType varchar(50),
					MainGroupID int,
					MainGroup varchar(50),
					SaleTypeID int,
					SaleType varchar(50),
					MenuItemID int,
					MenuItem varchar(50),
					MenuItemPLU varchar(16),
					DiscountID int,
					Discount varchar(50),
					Special int,
					WeekNumber int,
					WeekName varchar(20),
					WeekDayNumber int,
					WeekDayName varchar(16),
					DayNumber int,
					DayName varchar(20),
					BatchSubmissionStatus int,
					BSS1 int, BSS2 int, BSS3 int, BSS4 int, BSS5 int, BSS6 int, BSS7 int, BSS8 int, BSS9 int, BSS10 int,
						BSS11 int, BSS12 int, BSS13 int, BSS14 int, BSS15 int, BSS16 int, BSS17 int, BSS18 int, BSS19 int, BSS20 int,
						BSS21 int, BSS22 int, BSS23 int, BSS24 int, BSS25 int, BSS26 int, BSS27 int, BSS28 int, BSS29 int, BSS30 int,
						BSS31 int, BSS32 int, BSS33 int, BSS34 int, BSS35 int,
					D1 money, D2 money, D3 money, D4 money, D5 money, D6 money, D7 money, D8 money, D9 money, D10 money,
						D11 money, D12 money, D13 money, D14 money, D15 money, D16 money, D17 money, D18 money, D19 money, D20 money,
						D21 money, D22 money, D23 money, D24 money, D25 money, D26 money, D27 money, D28 money, D29 money, D30 money,
						D31 money, D32 money, D33 money, D34 money, D35 money,
					DS1 money, DS2 money, DS3 money, DS4 money, DS5 money, DS6 money, DS7 money, DS8 money, DS9 money, DS10 money, 
						DS11 money, DS12 money, DS13 money, DS14 money, DS15 money, DS16 money, DS17 money, DS18 money, DS19 money, DS20 money,
						DS21 money, DS22 money, DS23 money, DS24 money, DS25 money, DS26 money, DS27 money, DS28 money, DS29 money, DS30 money,
						DS31 money, DS32 money, DS33 money, DS34 money, DS35 money,
					N1 money, N2 money, N3 money, N4 money, N5 money, N6 money, N7 money, N8 money, N9 money, N10 money, 
						N11 money, N12 money, N13 money, N14 money, N15 money, N16 money, N17 money, N18 money, N19 money, N20 money,
						N21 money, N22 money, N23 money, N24 money, N25 money, N26 money, N27 money, N28 money, N29 money, N30 money,
						N31 money, N32 money, N33 money, N34 money, N35 money, 
					Q1 money, Q2 money, Q3 money, Q4 money, Q5 money, Q6 money, Q7 money, Q8 money, Q9 money, Q10 money, 
						Q11 money, Q12 money, Q13 money, Q14 money, Q15 money, Q16 money, Q17 money, Q18 money, Q19 money, Q20 money,
						Q21 money, Q22 money, Q23 money, Q24 money, Q25 money, Q26 money, Q27 money, Q28 money, Q29 money, Q30 money,
						Q31 money, Q32 money, Q33 money, Q34 money, Q35 money");
			if (CostMode>0)
				sbSQLData.Append (@",
					C1 money, C2 money, C3 money, C4 money, C5 money, C6 money, C7 money, C8 money, C9 money, C10 money, 
						C11 money, C12 money, C13 money, C14 money, C15 money, C16 money, C17 money, C18 money, C19 money, C20 money,
						C21 money, C22 money, C23 money, C24 money, C25 money, C26 money, C27 money, C28 money, C29 money, C30 money,
						C31 money, C32 money, C33 money, C34 money, C35 money");
			sbSQLData.Append (@",
				VOIDS1 money, VOIDS2 money, VOIDS3 money, VOIDS4 money, VOIDS5 money, VOIDS6 money, VOIDS7 money, VOIDS8 money, VOIDS9 money, VOIDS10 money, 
					VOIDS11 money, VOIDS12 money, VOIDS13 money, VOIDS14 money, VOIDS15 money, VOIDS16 money, VOIDS17 money, VOIDS18 money, VOIDS19 money, VOIDS20 money,
					VOIDS21 money, VOIDS22 money, VOIDS23 money, VOIDS24 money, VOIDS25 money, VOIDS26 money, VOIDS27 money, VOIDS28 money, VOIDS29 money, VOIDS30 money,
					VOIDS31 money, VOIDS32 money, VOIDS33 money, VOIDS34 money, VOIDS35 money");
			sbSQLData.Append (@",
					TotalSales money,
					Discounts money,
					NetSales money,
					Quantity money,
					TotalVoids money,
					TotalRefunds money,
					TotalSelectedCharges1 money,
					TotalSelectedCharges2 money,
					TotalSelectedCharges0 money");
			if (CostMode==0)
				sbSQLData.Append (@",
					RoyaltyFoodSales money,
					RoyaltyBeverageSales money,
					RoyaltyOtherSales money,
					RoyaltyDiscounts money,
					RoyaltyExcludedDiscounts money");
			if (CostMode>0)
				sbSQLData.Append(@",
					MenuItemPrice money, 
					ProductCost money,
					UnitCost money,
					UnitCostSource int,
					GrossCostPercentage money,
					DiscountsPercentage money,
					AdjustedCost money,
					AdjustedCostNetCostPercentage money,
					AdjustedProfit money,
					AdjustedProfitNetCostPercentage money");
			sbSQLData.Append("\n\t\t\t\t\t)\n\n");
			sbSQLData.Append(@"				declare @tblCashAndSalesSales table (
					--SalesKey bigint PRIMARY KEY NOT NULL,
					BatchID int,
					CompareIndex smallint,
					DayNumber int,
					StoreID int,
					ProfitCenterID int,
					MainTypeID int,
					MainGroupID int,
					SaleTypeID int,
					PLU int,
					Discount int,
					Special bit,
					WeekNumber smallint,
					WeekDayNumber smallint,"
						+ ((CostMode==2)?"\n\t\t\t\t\tItemCost money,":"") + @"
					SaleItemID int,
					Amount money,
					Quantity money,
					ItemPrice money,
					BatchSubmissionStatus int,
					DiscountAmount money
					)"+"\n");
			sbSQLData.Append ("\n\n\t\t\tinsert into @temp_table");
			if (CostMode>0) {
				sbSQLData.Append ("\n" + @"			select
				ct.CompareIndex,
				DayPartID,
				DayPart,
				DayPartSort,
				DayHourNumber,
				DayHourName,
				FrequentDinerPlanID,
				FrequentDinerPlan,
				FrequentDinerCustomerID,
				FrequentDinerCustomer,
				StoreStaffID,
				StoreStaff,
				CheckNumberID,
				CheckNumber,
				StoreGroupID,
				StoreGroup,
				StoreGroupSort,
				StoreID,
				StoreNumber,
				StoreName,
				StoreGCode,
				ProfitCenterID,                         
				ProfitCenter,
				MainTypeID, 
				MainType,
				MainGroupID, MainGroup,
				SaleTypeID, SaleType,
				MenuItemID, MenuItem, MenuItemPLU,
				DiscountID, Discount,
				Special,
				WeekNumber, WeekName,
				WeekDayNumber, WeekDayName,
				DayNumber, DayName,
				max(BatchSubmissionStatus) as BatchSubmissionStatus,
				max(BSS1) as BSS1, max(BSS2) as BSS2, max(BSS3) as BSS3, max(BSS4) as BSS4, max(BSS5) as BSS5, max(BSS6) as BSS6, max(BSS7) as BSS7, max(BSS8) as BSS8, max(BSS9) as BSS9, max(BSS10) as BSS10, 
					max(BSS11) as BSS11, max(BSS12) as BSS12, max(BSS13) as BSS13, max(BSS14) as BSS14, max(BSS15) as BSS15, max(BSS16) as BSS16, max(BSS17) as BSS17, max(BSS18) as BSS18, max(BSS19) as BSS19, max(BSS20) as BSS20, 
					max(BSS21) as BSS21, max(BSS22) as BSS22, max(BSS23) as BSS23, max(BSS24) as BSS24, max(BSS25) as BSS25, max(BSS26) as BSS26, max(BSS27) as BSS27, max(BSS28) as BSS28, max(BSS29) as BSS29, max(BSS30) as BSS30, 
					max(BSS31) as BSS31, max(BSS32) as BSS32, max(BSS33) as BSS33, max(BSS34) as BSS34, max(BSS35) as BSS35,
				sum(D1) as D1, sum(D2) as D2, sum(D3) as D3, sum(D4) as D4, sum(D5) as D5, sum(D6) as D6, sum(D7) as D7, sum(D8) as D8, sum(D9) as D9, sum(D10) as D10,
					sum(D11) as D11, sum(D12) as D12, sum(D13) as D13, sum(D14) as D14, sum(D15) as D15, sum(D16) as D16, sum(D17) as D17, sum(D18) as D18, sum(D19) as D19, sum(D20) as D20,
					sum(D21) as D21, sum(D22) as D22, sum(D23) as D23, sum(D24) as D24, sum(D25) as D25, sum(D26) as D26, sum(D27) as D27, sum(D28) as D28, sum(D29) as D29, sum(D30) as D30,
					sum(D31) as D31, sum(D32) as D32, sum(D33) as D33, sum(D34) as D34, sum(D35) as D35,
				sum(DS1) as DS1, sum(DS2) as DS2, sum(DS3) as DS3, sum(DS4) as DS4, sum(DS5) as DS5, sum(DS6) as DS6, sum(DS7) as DS7, sum(DS8) as DS8, sum(DS9) as DS9, sum(DS10) as DS10,
					sum(DS11) as DS11, sum(DS12) as DS12, sum(DS13) as DS13, sum(DS14) as DS14, sum(DS15) as DS15, sum(DS16) as DS16, sum(DS17) as DS17, sum(DS18) as DS18, sum(DS19) as DS19, sum(DS20) as DS20,
					sum(DS21) as DS21, sum(DS22) as DS22, sum(DS23) as DS23, sum(DS24) as DS24, sum(DS25) as DS25, sum(DS26) as DS26, sum(DS27) as DS27, sum(DS28) as DS28, sum(DS29) as DS29, sum(DS30) as DS30,
					sum(DS31) as DS31, sum(DS32) as DS32, sum(DS33) as DS33, sum(DS34) as DS34, sum(DS35) as DS35,
				sum(N1) as N1, sum(N2) as N2, sum(N3) as N3, sum(N4) as N4, sum(N5) as N5, sum(N6) as N6, sum(N7) as N7, sum(N8) as N8, sum(N9) as N9, sum(N10) as N10,
					sum(N11) as N11, sum(N12) as N12, sum(N13) as N13, sum(N14) as N14, sum(N15) as N15, sum(N16) as N16, sum(N17) as N17, sum(N18) as N18, sum(N19) as N19, sum(N20) as N20,
					sum(N21) as N21, sum(N22) as N22, sum(N23) as N23, sum(N24) as N24, sum(N25) as N25, sum(N26) as N26, sum(N27) as N27, sum(N28) as N28, sum(N29) as N29, sum(N30) as N30,
					sum(N31) as N31, sum(N32) as N32, sum(N33) as N33, sum(N34) as N34, sum(N35) as N35,
				sum(Q1) as Q1, sum(Q2) as Q2, sum(Q3) as Q3, sum(Q4) as Q4, sum(Q5) as Q5, sum(Q6) as Q6, sum(Q7) as Q7, sum(Q8) as Q8, sum(Q9) as Q9, sum(Q10) as Q10,
					sum(Q11) as Q11, sum(Q12) as Q12, sum(Q13) as Q13, sum(Q14) as Q14, sum(Q15) as Q15, sum(Q16) as Q16, sum(Q17) as Q17, sum(Q18) as Q18, sum(Q19) as Q19, sum(Q20) as Q20,
					sum(Q21) as Q21, sum(Q22) as Q22, sum(Q23) as Q23, sum(Q24) as Q24, sum(Q25) as Q25, sum(Q26) as Q26, sum(Q27) as Q27, sum(Q28) as Q28, sum(Q29) as Q29, sum(Q30) as Q30,
					sum(Q31) as Q31, sum(Q32) as Q32, sum(Q33) as Q33, sum(Q34) as Q34, sum(Q35) as Q35,
				sum(C1) as C1, sum(C2) as C2, sum(C3) as C3, sum(C4) as C4, sum(C5) as C5, sum(C6) as C6, sum(C7) as C7, sum(C8) as C8, sum(C9) as C9, sum(C10) as C10,
					sum(C11) as C11, sum(C12) as C12, sum(C13) as C13, sum(C14) as C14, sum(C15) as C15, sum(C16) as C16, sum(C17) as C17, sum(C18) as C18, sum(C19) as C19, sum(C20) as C20,
					sum(C21) as C21, sum(C22) as C22, sum(C23) as C23, sum(C24) as C24, sum(C25) as C25, sum(C26) as C26, sum(C27) as C27, sum(C28) as C28, sum(C29) as C29, sum(C30) as C30,
					sum(C31) as C31, sum(C32) as C32, sum(C33) as C33, sum(C34) as C34, sum(C35) as C35");
			sbSQLData.Append(@",
				isnull(VOIDS1,0) as VOIDS1, isnull(VOIDS2,0) as VOIDS2, isnull(VOIDS3,0) as VOIDS3, isnull(VOIDS4,0) as VOIDS4, isnull(VOIDS5,0) as VOIDS5, isnull(VOIDS6,0) as VOIDS6, isnull(VOIDS7,0) as VOIDS7, isnull(VOIDS8,0) as VOIDS8, isnull(VOIDS9,0) as VOIDS9, isnull(VOIDS10,0) as VOIDS10,
					isnull(VOIDS11,0) as VOIDS11, isnull(VOIDS12,0) as VOIDS12, isnull(VOIDS13,0) as VOIDS13, isnull(VOIDS14,0) as VOIDS14, isnull(VOIDS15,0) as VOIDS15, isnull(VOIDS16,0) as VOIDS16, isnull(VOIDS17,0) as VOIDS17, isnull(VOIDS18,0) as VOIDS18, isnull(VOIDS19,0) as VOIDS19, isnull(VOIDS20,0) as VOIDS20,
					isnull(VOIDS21,0) as VOIDS21, isnull(VOIDS22,0) as VOIDS22, isnull(VOIDS23,0) as VOIDS23, isnull(VOIDS24,0) as VOIDS24, isnull(VOIDS25,0) as VOIDS25, isnull(VOIDS26,0) as VOIDS26, isnull(VOIDS27,0) as VOIDS27, isnull(VOIDS28,0) as VOIDS28, isnull(VOIDS29,0) as VOIDS29, isnull(VOIDS30,0) as VOIDS30,
					isnull(VOIDS31,0) as VOIDS31, isnull(VOIDS32,0) as VOIDS32, isnull(VOIDS33,0) as VOIDS33, isnull(VOIDS34,0) as VOIDS34, isnull(VOIDS35,0) as VOIDS35");
			sbSQLData.Append(@",
				sum(TotalSales) as TotalSales,
				sum(Discounts) as Discounts,
				sum(NetSales) as NetSales,
				sum(Quantity) as Quantity,
				sum(TotalVoids) as TotalVoids,
				sum(TotalRefunds) as TotalRefunds,
				sum(TotalSelectedCharges1) as TotalSelectedCharges1,
				sum(TotalSelectedCharges2) as TotalSelectedCharges2,
				sum(TotalSelectedCharges0) as TotalSelectedCharges0,
				case when sum(Quantity)<>0 then sum(TotalSales)/sum(Quantity) else null end as MenuItemPrice,
				sum(ProductCost) as ProductCost,
				case when sum(Quantity)<>0 then sum(ProductCost)/sum(Quantity) else null end as UnitCost,
				max(InventoryCostSource & 1) + max(InventoryCostSource & 2) + max(InventoryCostSource & 4) as UnitCostSource,
				case when sum(TotalSales)<>0 then 100*sum(ProductCost)/sum(TotalSales) else null end as GrossCostPercentage,
				case when sum(TotalSales)<>0 then 100*sum(Discounts)/sum(TotalSales) else null end as DiscountsPercentage,
				sum(TotalSales)*(100-case when sum(TotalSales)<>0 then 100*sum(Discounts)/sum(TotalSales) else 0 end) as AdjustedCost,
				case when sum(TotalSales+Discounts)<>0
					then 100*sum(TotalSales)*(100-case when sum(TotalSales)<>0 then 100*sum(Discounts)/sum(TotalSales) else 0 end)/sum(TotalSales+Discounts)
					else null
				end as AdjustedCostNetCostPercentage,
				sum(TotalSales+Discounts)-(sum(TotalSales)*(100-case when sum(TotalSales)<>0 then 100*sum(Discounts)/sum(TotalSales) else 0 end)) as AdjustedProfit,
				case when sum(TotalSales+Discounts)<>0
					then 100-(100*sum(TotalSales)*(100-case when sum(TotalSales)<>0 then 100*sum(Discounts)/sum(TotalSales) else 0 end)/sum(TotalSales+Discounts))
					else null
				end as AdjustedProfitNetCostPercentage
			from (");
				}
			sbSQLData.Append (@"
				select" + "\n");
			sbSQLData.Append (@"					cs.CompareIndex," + "\n");
			if (DayPartFlag==1)
				sbSQLData.Append (@"				  cs.DayPartID,
					dpt.DayPart as DayPart,
					cs.DayPartID as DayPartSort," + "\n");
			else
				sbSQLData.Append (@"					null as DayPartID,
					null as DayPart,
					null as DayPartSort," + "\n");
			if (DayHourFlag==1)
				sbSQLData.Append (@"				  cs.DayHourNumber,
					dht.DayHourName," + "\n");
			else
				sbSQLData.Append (@"					null as DayHourNumber,
					null as DayHourName," + "\n");
			if (FrequentDinerPlanFlag==1)
				sbSQLData.Append (@"				  cs.FrequentDinerPlanID,
					tblFrequentDinerPlans.FrequentDinerPlan as FrequentDinerPlan," + "\n");
			else
				sbSQLData.Append (@"					null as FrequentDinerPlanID,
					null as FrequentDinerPlan," + "\n");
			if (FrequentDinerCustomerFlag==1)
				sbSQLData.Append (@"				  cs.FrequentDinerCustomerID,
					isnull(tblFrequentDinerCustomers.LastName+', ','')+isnull(tblFrequentDinerCustomers.FirstName,'') as FrequentDinerCustomer," + "\n");
			else
				sbSQLData.Append (@"					null as FrequentDinerCustomerID,
					null as FrequentDinerCustomer," + "\n");
			if (StoreStaffFlag==1)
				sbSQLData.Append (@"				  cs.StoreStaffID,
					isnull(ltrim(rtrim(tblStaff.LastName))+', ','')+isnull(ltrim(rtrim(tblStaff.FirstName)),'') as StoreStaff," + "\n");
			else
				sbSQLData.Append (@"					null as StoreStaffID,
					null as StoreStaff," + "\n");
			if (CheckNumberFlag==1)
				sbSQLData.Append (@"				  cs.CheckNumberID,
					cast(floor(cs.CheckNumberID/10000000000) as varchar(10)) + '-' + cast(floor(cs.CheckNumberID/100)%1000000 as varchar(10)) + '-' + cast(cs.CheckNumberID%100 as varchar(9)) as CheckNumber," + "\n");
			else
				sbSQLData.Append (@"					null as CheckNumberID,
					null as CheckNumber," + "\n");
			if (StoreGroupFlag==1)
				sbSQLData.Append (@"				  tblStores.StoreGroupID,
					tblStoreGroups.StoreGroupName as StoreGroup,
					tblStoreGroups.DisplayOrder as StoreGroupSort," + "\n");
			else
				sbSQLData.Append (@"					null as StoreGroupID,
					null as StoreGroup,
					null as StoreGroupSort," + "\n");
			if (StoreFlag==1 || ProfitCenterFlag==1)
				sbSQLData.Append (@"					cs.StoreID,
					tblStores.StoreNumber,
					isnull(tblStores.StoreReportName,tblStores.StoreName) as StoreName,
					tblStores.StoreGCode," + "\n");
			else
				sbSQLData.Append (@"					null as StoreID,
					null as StoreNumber,
					null as StoreName,
					null as StoreGCode," + "\n");
			if (ProfitCenterFlag==1)
				sbSQLData.Append (@"					cs.ProfitCenterID,
					case when tblProfitCenters.ProfitCenterGroup is null then tblProfitCenters.ProfitCenter else tblProfitCenters.ProfitCenterGroup end as ProfitCenter," + "\n");
			else
				sbSQLData.Append (@"					null as ProfitCenterID,                         
					null as ProfitCenter," + "\n");
			if (MainTypeFlag==1)
				sbSQLData.Append (@"					cs.MainTypeID, 
					mt.Description as MainType," + "\n");
			else
				sbSQLData.Append (@"					null as MainTypeID, 
					null as MainType," + "\n");
			if (MainGroupFlag==1)
				sbSQLData.Append (@"					cs.MainGroupID,
					mg.MainGroup," + "\n");
			else
				sbSQLData.Append (@"					null as MainGroupID,
					null as MainGroup," + "\n");
			if (SaleTypeFlag==1)
				sbSQLData.Append (@"					cs.SaleTypeID,
					case when st.SaleTypeGroup is null then st.SaleType else st.SaleTypeGroup end as SaleType," + "\n");
			else
				sbSQLData.Append (@"					null as SaleTypeID,
					null as SaleType," + "" + "\n");
			if (MenuItemFlag==1 && MenuItemPLUFlag==1) {
				sbSQLData.Append (@"					cs.MenuItemID,
					case
						when cs.MenuItemID=0
							then '&lt;Special Item&gt;'
						when cs.MenuItemID>10000000
							then '&lt;Service Charge&gt;'
						when mi.Description is null
							then '&lt;PLU: '+cast(cs.MenuItemID as varchar(12))+'&gt;'
						else
							mi.Description
					end as MenuItem,
					cast(cs.MenuItemID as varchar(16)) as MenuItemPLU," + "\n");
				}
			else if (MenuItemFlag==1) {
				sbSQLData.Append (@"					null as MenuItemID,
					case
						when cs.MenuItemID=0
							then '&lt;Special Item&gt;'
						when cs.MenuItemID>10000000
							then '&lt;Service Charge&gt;'
						when mi.Description is null
							then '&lt;PLU: '+cast(cs.MenuItemID as varchar(12))+'&gt;'
						else
							mi.Description
					end as MenuItem,
					null as MenuItemPLU," + "\n");
				}
			else if (MenuItemPLUFlag==1) {
				sbSQLData.Append (@"					cs.MenuItemID,
					null as MenuItem,
					cast(cs.MenuItemID as varchar(16)) as MenuItemPLU," + "\n");
				}
			else {
				sbSQLData.Append (@"					null as MenuItemID,
					null as MenuItem,
					null as MenuItemPLU," + "\n");
				}
			if (DiscountFlag==1)
				sbSQLData.Append (@"					cs.DiscountID,
					dsc.Discount," + "\n");
			else
				sbSQLData.Append (@"					null as DiscountID,
					null as Discount," + "\n");
			if (SpecialFlag==1)
				sbSQLData.Append (@"					cs.Special," + "\n");
			else
				sbSQLData.Append (@"					null as Special," + "\n");
			if (WeekFlag==1)
				sbSQLData.Append (@"          cs.WeekNumber,
					substring(cast(dbo.fn_CalcDayNumberDate(cs.WeekNumber*7) as varchar(20)),1,3) + ' '
						+ cast(day(dbo.fn_CalcDayNumberDate(cs.WeekNumber*7)) as varchar(10)) as WeekName," + "\n");
			else
				sbSQLData.Append (@"          null as WeekNumber,
					null as WeekName," + "\n");
			if (WeekDayFlag==1)
				sbSQLData.Append (@"          cs.WeekDayNumber,
					wd.WeekDayName as WeekDayName," + "\n");
			else
				sbSQLData.Append (@"          null as WeekDayNumber,
					null as WeekDayName," + "\n");
			if (DayFlag==1)
				sbSQLData.Append (@"          cs.DayNumber,
					substring(cast(dbo.fn_CalcDayNumberDate(cs.DayNumber) as varchar(20)),1,3) + ' '
						+ cast(day(dbo.fn_CalcDayNumberDate(cs.DayNumber)) as varchar(10)) as DayName,");
			else
				sbSQLData.Append (@"          null as DayNumber,
					null as DayName,");
			sbSQLData.Append(@"
					max(BatchSubmissionStatus) as BatchSubmissionStatus,");
			sbSQLData.Append (@"
					max(BSS1) as BSS1, max(BSS2) as BSS2, max(BSS3) as BSS3, max(BSS4) as BSS4, max(BSS5) as BSS5, max(BSS6) as BSS6, max(BSS7) as BSS7, max(BSS8) as BSS8, max(BSS9) as BSS9, max(BSS10) as BSS10, 
						max(BSS11) as BSS11, max(BSS12) as BSS12, max(BSS13) as BSS13, max(BSS14) as BSS14, max(BSS15) as BSS15, max(BSS16) as BSS16, max(BSS17) as BSS17, max(BSS18) as BSS18, max(BSS19) as BSS19, max(BSS20) as BSS20, 
						max(BSS21) as BSS21, max(BSS22) as BSS22, max(BSS23) as BSS23, max(BSS24) as BSS24, max(BSS25) as BSS25, max(BSS26) as BSS26, max(BSS27) as BSS27, max(BSS28) as BSS28, max(BSS29) as BSS29, max(BSS30) as BSS30, 
						max(BSS31) as BSS31, max(BSS32) as BSS32, max(BSS33) as BSS33, max(BSS34) as BSS34, max(BSS35) as BSS35,
					sum(D1) as D1, sum(D2) as D2, sum(D3) as D3, sum(D4) as D4, sum(D5) as D5, sum(D6) as D6, sum(D7) as D7, sum(D8) as D8, sum(D9) as D9, sum(D10) as D10, 
						sum(D11) as D11, sum(D12) as D12, sum(D13) as D13, sum(D14) as D14, sum(D15) as D15, sum(D16) as D16, sum(D17) as D17, sum(D18) as D18, sum(D19) as D19, sum(D20) as D20, 
						sum(D21) as D21, sum(D22) as D22, sum(D23) as D23, sum(D24) as D24, sum(D25) as D25, sum(D26) as D26, sum(D27) as D27, sum(D28) as D28, sum(D29) as D29, sum(D30) as D30, 
						sum(D31) as D31, sum(D32) as D32, sum(D33) as D33, sum(D34) as D34, sum(D35) as D35,
					sum(DS1) as DS1, sum(DS2) as DS2, sum(DS3) as DS3, sum(DS4) as DS4, sum(DS5) as DS5, sum(DS6) as DS6, sum(DS7) as DS7, sum(DS8) as DS8, sum(DS9) as DS9, sum(DS10) as DS10, 
						sum(DS11) as DS11, sum(DS12) as DS12, sum(DS13) as DS13, sum(DS14) as DS14, sum(DS15) as DS15, sum(DS16) as DS16, sum(DS17) as DS17, sum(DS18) as DS18, sum(DS19) as DS19, sum(DS20) as DS20, 
						sum(DS21) as DS21, sum(DS22) as DS22, sum(DS23) as DS23, sum(DS24) as DS24, sum(DS25) as DS25, sum(DS26) as DS26, sum(DS27) as DS27, sum(DS28) as DS28, sum(DS29) as DS29, sum(DS30) as DS30, 
						sum(DS31) as DS31, sum(DS32) as DS32, sum(DS33) as DS33, sum(DS34) as DS34, sum(DS35) as DS35,
					sum(N1) as N1, sum(N2) as N2, sum(N3) as N3, sum(N4) as N4, sum(N5) as N5, sum(N6) as N6, sum(N7) as N7, sum(N8) as N8, sum(N9) as N9, sum(N10) as N10, 
						sum(N11) as N11, sum(N12) as N12, sum(N13) as N13, sum(N14) as N14, sum(N15) as N15, sum(N16) as N16, sum(N17) as N17, sum(N18) as N18, sum(N19) as N19, sum(N20) as N20, 
						sum(N21) as N21, sum(N22) as N22, sum(N23) as N23, sum(N24) as N24, sum(N25) as N25, sum(N26) as N26, sum(N27) as N27, sum(N28) as N28, sum(N29) as N29, sum(N30) as N30, 
						sum(N31) as N31, sum(N32) as N32, sum(N33) as N33, sum(N34) as N34, sum(N35) as N35,
					sum(Q1) as Q1, sum(Q2) as Q2, sum(Q3) as Q3, sum(Q4) as Q4, sum(Q5) as Q5, sum(Q6) as Q6, sum(Q7) as Q7, sum(Q8) as Q8, sum(Q9) as Q9, sum(Q10) as Q10, 
						sum(Q11) as Q11, sum(Q12) as Q12, sum(Q13) as Q13, sum(Q14) as Q14, sum(Q15) as Q15, sum(Q16) as Q16, sum(Q17) as Q17, sum(Q18) as Q18, sum(Q19) as Q19, sum(Q20) as Q20, 
						sum(Q21) as Q21, sum(Q22) as Q22, sum(Q23) as Q23, sum(Q24) as Q24, sum(Q25) as Q25, sum(Q26) as Q26, sum(Q27) as Q27, sum(Q28) as Q28, sum(Q29) as Q29, sum(Q30) as Q30, 
						sum(Q31) as Q31, sum(Q32) as Q32, sum(Q33) as Q33, sum(Q34) as Q34, sum(Q35) as Q35,");
				if (IncludeVoids)
					sbSQLData.Append(@"
					sum(isnull(VOIDS1,0)) as VOIDS1, sum(isnull(VOIDS2,0)) as VOIDS2, sum(isnull(VOIDS3,0)) as VOIDS3, sum(isnull(VOIDS4,0)) as VOIDS4, sum(isnull(VOIDS5,0)) as VOIDS5, sum(isnull(VOIDS6,0)) as VOIDS6, sum(isnull(VOIDS7,0)) as VOIDS7, sum(isnull(VOIDS8,0)) as VOIDS8, sum(isnull(VOIDS9,0)) as VOIDS9, sum(isnull(VOIDS10,0)) as VOIDS10,
						sum(isnull(VOIDS11,0)) as VOIDS11, sum(isnull(VOIDS12,0)) as VOIDS12, sum(isnull(VOIDS13,0)) as VOIDS13, sum(isnull(VOIDS14,0)) as VOIDS14, sum(isnull(VOIDS15,0)) as VOIDS15, sum(isnull(VOIDS16,0)) as VOIDS16, sum(isnull(VOIDS17,0)) as VOIDS17, sum(isnull(VOIDS18,0)) as VOIDS18, sum(isnull(VOIDS19,0)) as VOIDS19, sum(isnull(VOIDS20,0)) as VOIDS20,
						sum(isnull(VOIDS21,0)) as VOIDS21, sum(isnull(VOIDS22,0)) as VOIDS22, sum(isnull(VOIDS23,0)) as VOIDS23, sum(isnull(VOIDS24,0)) as VOIDS24, sum(isnull(VOIDS25,0)) as VOIDS25, sum(isnull(VOIDS26,0)) as VOIDS26, sum(isnull(VOIDS27,0)) as VOIDS27, sum(isnull(VOIDS28,0)) as VOIDS28, sum(isnull(VOIDS29,0)) as VOIDS29, sum(isnull(VOIDS30,0)) as VOIDS30,
						sum(isnull(VOIDS31,0)) as VOIDS31, sum(isnull(VOIDS32,0)) as VOIDS32, sum(isnull(VOIDS33,0)) as VOIDS33, sum(isnull(VOIDS34,0)) as VOIDS34, sum(isnull(VOIDS35,0)) as VOIDS35,");
				else
					sbSQLData.Append(@"
					0 as VOIDS1, 0 as VOIDS2, 0 as VOIDS3, 0 as VOIDS4, 0 as VOIDS5, 0 as VOIDS6, 0 as VOIDS7, 0 as VOIDS8, 0 as VOIDS9, 0 as VOIDS10,
						0 as VOIDS11, 0 as VOIDS12, 0 as VOIDS13, 0 as VOIDS14, 0 as VOIDS15, 0 as VOIDS16, 0 as VOIDS17, 0 as VOIDS18, 0 as VOIDS19, 0 as VOIDS20,
						0 as VOIDS21, 0 as VOIDS22, 0 as VOIDS23, 0 as VOIDS24, 0 as VOIDS25, 0 as VOIDS26, 0 as VOIDS27, 0 as VOIDS28, 0 as VOIDS29, 0 as VOIDS30,
						0 as VOIDS31, 0 as VOIDS32, 0 as VOIDS33, 0 as VOIDS34, 0 as VOIDS35,");
			if (CostMode==1)
				sbSQLData.Append (@"
					sum(Q1)*cs.InventoryCost as C1,
						sum(Q2)*cs.InventoryCost as C2,
						sum(Q3)*cs.InventoryCost as C3,
						sum(Q4)*cs.InventoryCost as C4,
						sum(Q5)*cs.InventoryCost as C5,
						sum(Q6)*cs.InventoryCost as C6,
						sum(Q7)*cs.InventoryCost as C7,
						sum(Q8)*cs.InventoryCost as C8,
						sum(Q9)*cs.InventoryCost as C9,
						sum(Q10)*cs.InventoryCost as C10,
						sum(Q11)*cs.InventoryCost as C11,
						sum(Q12)*cs.InventoryCost as C12,
						sum(Q13)*cs.InventoryCost as C13,
						sum(Q14)*cs.InventoryCost as C14,
						sum(Q15)*cs.InventoryCost as C15,
						sum(Q16)*cs.InventoryCost as C16,
						sum(Q17)*cs.InventoryCost as C17,
						sum(Q18)*cs.InventoryCost as C18,
						sum(Q19)*cs.InventoryCost as C19,
						sum(Q20)*cs.InventoryCost as C20,
						sum(Q21)*cs.InventoryCost as C21,
						sum(Q22)*cs.InventoryCost as C22,
						sum(Q23)*cs.InventoryCost as C23,
						sum(Q24)*cs.InventoryCost as C24,
						sum(Q25)*cs.InventoryCost as C25,
						sum(Q26)*cs.InventoryCost as C26,
						sum(Q27)*cs.InventoryCost as C27,
						sum(Q28)*cs.InventoryCost as C28,
						sum(Q29)*cs.InventoryCost as C29,
						sum(Q30)*cs.InventoryCost as C30,
						sum(Q31)*cs.InventoryCost as C31,
						sum(Q32)*cs.InventoryCost as C32,
						sum(Q33)*cs.InventoryCost as C33,
						sum(Q34)*cs.InventoryCost as C34,
						sum(Q35)*cs.InventoryCost as C35,");
			else if (CostMode==2)
				sbSQLData.Append (@"
					sum(C1) as C1, sum(C2) as C2, sum(C3) as C3, sum(C4) as C4, sum(C5) as C5, sum(C6) as C6, sum(C7) as C7, sum(C8) as C8, sum(C9) as C9, sum(C10) as C10,
						sum(C11) as C11, sum(C12) as C12, sum(C13) as C13, sum(C14) as C14, sum(C15) as C15, sum(C16) as C16, sum(C17) as C17, sum(C18) as C18, sum(C19) as C19, sum(C20) as C20,
						sum(C21) as C21, sum(C22) as C22, sum(C23) as C23, sum(C24) as C24, sum(C25) as C25, sum(C26) as C26, sum(C27) as C27, sum(C28) as C28, sum(C29) as C29, sum(C30) as C30,
						sum(C31) as C31, sum(C32) as C32, sum(C33) as C33, sum(C34) as C34, sum(C35) as C35,
						");
			sbSQLData.Append (@"
					sum(TotalSales) as TotalSales,
					sum(Discounts) as Discounts,
					sum(NetSales) as NetSales,
					sum(Quantity) as Quantity,");
			if (IncludeVoids)
				sbSQLData.Append (@"
					sum(isnull(TotalVoids,0)) as TotalVoids,
					sum(isnull(TotalRefunds,0)) as TotalRefunds,");
			else
				sbSQLData.Append (@"
					0 as TotalVoids,
					0 as TotalRefunds,");
			sbSQLData.Append(@"
					sum(isnull(cq.TotalSelectedChargesAmount1,0)) as TotalSelectedCharges1,
					sum(isnull(cq.TotalSelectedChargesAmount2,0)) as TotalSelectedCharges2,
					sum(isnull(cq.TotalSelectedChargesAmount0,0)) as TotalSelectedCharges0");
			if (CostMode==0)
				sbSQLData.Append (@",
					sum(RoyaltyFoodSales) as RoyaltyFoodSales,
					sum(RoyaltyBeverageSales) as RoyaltyBeverageSales,
					sum(RoyaltyOtherSales) as RoyaltyOtherSales,
					sum(RoyaltyDiscounts) as RoyaltyDiscounts,
					sum(RoyaltyExcludedDiscounts) as RoyaltyExcludedDiscounts");
			if (CostMode==1)
				if (MenuItemFlag==1)
					sbSQLData.Append (@",
					mi.RegularPrice as MenuItemPrice,
					sum(Quantity)*cs.InventoryCost as ProductCost,
					cs.InventoryCost as UnitCost,
					Max(InventoryCostSource & 1) + max(InventoryCostSource & 2) + max(InventoryCostSource & 4) as InventoryCostSource");
				else
					sbSQLData.Append (@",
					null as MenuItemPrice,
					sum(Quantity)*cs.InventoryCost as ProductCost,
					cs.InventoryCost as UnitCost,
					Max(InventoryCostSource & 1) + max(InventoryCostSource & 2) + max(InventoryCostSource & 4) as InventoryCostSource");
			else if (CostMode==2)
				if (MenuItemFlag==1)
					sbSQLData.Append (@",
					mi.RegularPrice as MenuItemPrice,
					sum(ProductCost) as ProductCost,
					case when sum(Quantity)<>0 then floor(100*sum(ProductCost)/sum(Quantity))/100 else null end as UnitCost,
					Max(InventoryCostSource & 1) + max(InventoryCostSource & 2) + max(InventoryCostSource & 4) as InventoryCostSource");
				else
					sbSQLData.Append (@",
					null as MenuItemPrice,
					sum(ProductCost) as ProductCost,
					case when sum(Quantity)<>0 then floor(100*sum(ProductCost)/sum(Quantity))/100 else null end as UnitCost,
					Max(InventoryCostSource & 1) + max(InventoryCostSource & 2) + max(InventoryCostSource & 4) as InventoryCostSource");
			sbSQLData.Append (@"
				from (
					select" + "\n");
				sbSQLData.Append (@"						css.CompareIndex," + "\n");
			if (DayPartFlag==1)
				sbSQLData.Append (@"						css.DayPartID as DayPartID," + "\n");
			if (DayHourFlag==1)
				sbSQLData.Append (@"						css.DayHourNumber as DayHourNumber," + "\n");
			if (FrequentDinerPlanFlag==1)
				sbSQLData.Append (@"						css.FrequentDinerPlanID as FrequentDinerPlanID," + "\n");
			if (FrequentDinerCustomerFlag==1)
				sbSQLData.Append (@"						css.FrequentDinerCustomerID as FrequentDinerCustomerID," + "\n");
			if (StoreStaffFlag==1)
				sbSQLData.Append (@"						css.StoreStaffID as StoreStaffID," + "\n");
			if (CheckNumberFlag==1)
				sbSQLData.Append (@"						css.CheckNumberID as CheckNumberID," + "\n");
			if (StoreGroupFlag==1 || StoreGroupListCSV.Length>0 || StoreFlag==1 || ProfitCenterFlag==1
					|| MainGroupFlag==1 || SaleTypeFlag==1 || DiscountFlag==1 || MenuItemFlag==1 
					|| MenuItemPLUFlag==1 || SpecialFlag==1 || CostMode>0 || FrequentDinerPlanFlag==1
					|| FrequentDinerCustomerFlag==1 || StoreStaffFlag==1
					|| CheckNumberFlag==1 || DiscountListCSV.Length>0 || ColumnNumberExpressionRequiresDetailJoin)
				sbSQLData.Append ("\t\t\t\t\t\tcss.StoreID as StoreID," + "\n");
			if (ProfitCenterFlag==1)
				sbSQLData.Append ("\t\t\t\t\t\tcss.ProfitCenterID as ProfitCenterID," + "\n");
			if (MainTypeFlag==1)
				sbSQLData.Append (@"						css.MainTypeID as MainTypeID," + "\n");
			if (MainGroupFlag==1)
				sbSQLData.Append (@"						css.MainGroupID as MainGroupID," + "\n");
			if (SaleTypeFlag==1)
				sbSQLData.Append (@"						css.SaleTypeID as SaleTypeID," + "\n");
			if (MenuItemFlag==1 || MenuItemPLUFlag==1 || CostMode==1 || CostMode==3)
				sbSQLData.Append (@"						css.PLU as MenuItemID," + "\n");
			if (DiscountFlag==1)
				sbSQLData.Append (@"						css.Discount as DiscountID," + "\n");
			if (SpecialFlag==1)
				sbSQLData.Append (@"						css.Special as Special," + "\n");
			if (WeekFlag==1)
				sbSQLData.Append (@"						css.WeekNumber as WeekNumber," + "\n");
			if (WeekDayFlag==1)
				sbSQLData.Append (@"						css.WeekDayNumber as WeekDayNumber," + "\n");
			if (DayFlag==1)
				sbSQLData.Append (@"						css.DayNumber as DayNumber," + "\n");
			if (ShowBatchSubmissionStatus)
				sbSQLData.Append (@"						max(css.BatchSubmissionStatus) as BatchSubmissionStatus,
						max(case when " + ColumnNumberExpression + @" = 1 then css.BatchSubmissionStatus else 0 end) as BSS1,
						max(case when " + ColumnNumberExpression + @" = 2 then css.BatchSubmissionStatus else 0 end) as BSS2,
						max(case when " + ColumnNumberExpression + @" = 3 then css.BatchSubmissionStatus else 0 end) as BSS3,
						max(case when " + ColumnNumberExpression + @" = 4 then css.BatchSubmissionStatus else 0 end) as BSS4,
						max(case when " + ColumnNumberExpression + @" = 5 then css.BatchSubmissionStatus else 0 end) as BSS5,
						max(case when " + ColumnNumberExpression + @" = 6 then css.BatchSubmissionStatus else 0 end) as BSS6,
						max(case when " + ColumnNumberExpression + @" = 7 then css.BatchSubmissionStatus else 0 end) as BSS7,
						max(case when " + ColumnNumberExpression + @" = 8 then css.BatchSubmissionStatus else 0 end) as BSS8,
						max(case when " + ColumnNumberExpression + @" = 9 then css.BatchSubmissionStatus else 0 end) as BSS9,
						max(case when " + ColumnNumberExpression + @" = 10 then css.BatchSubmissionStatus else 0 end) as BSS10,
						max(case when " + ColumnNumberExpression + @" = 11 then css.BatchSubmissionStatus else 0 end) as BSS11,
						max(case when " + ColumnNumberExpression + @" = 12 then css.BatchSubmissionStatus else 0 end) as BSS12,
						max(case when " + ColumnNumberExpression + @" = 13 then css.BatchSubmissionStatus else 0 end) as BSS13,
						max(case when " + ColumnNumberExpression + @" = 14 then css.BatchSubmissionStatus else 0 end) as BSS14,
						max(case when " + ColumnNumberExpression + @" = 15 then css.BatchSubmissionStatus else 0 end) as BSS15,
						max(case when " + ColumnNumberExpression + @" = 16 then css.BatchSubmissionStatus else 0 end) as BSS16,
						max(case when " + ColumnNumberExpression + @" = 17 then css.BatchSubmissionStatus else 0 end) as BSS17,
						max(case when " + ColumnNumberExpression + @" = 18 then css.BatchSubmissionStatus else 0 end) as BSS18,
						max(case when " + ColumnNumberExpression + @" = 19 then css.BatchSubmissionStatus else 0 end) as BSS19,
						max(case when " + ColumnNumberExpression + @" = 20 then css.BatchSubmissionStatus else 0 end) as BSS20,
						max(case when " + ColumnNumberExpression + @" = 21 then css.BatchSubmissionStatus else 0 end) as BSS21,
						max(case when " + ColumnNumberExpression + @" = 22 then css.BatchSubmissionStatus else 0 end) as BSS22,
						max(case when " + ColumnNumberExpression + @" = 23 then css.BatchSubmissionStatus else 0 end) as BSS23,
						max(case when " + ColumnNumberExpression + @" = 24 then css.BatchSubmissionStatus else 0 end) as BSS24,
						max(case when " + ColumnNumberExpression + @" = 25 then css.BatchSubmissionStatus else 0 end) as BSS25,
						max(case when " + ColumnNumberExpression + @" = 26 then css.BatchSubmissionStatus else 0 end) as BSS26,
						max(case when " + ColumnNumberExpression + @" = 27 then css.BatchSubmissionStatus else 0 end) as BSS27,
						max(case when " + ColumnNumberExpression + @" = 28 then css.BatchSubmissionStatus else 0 end) as BSS28,
						max(case when " + ColumnNumberExpression + @" = 29 then css.BatchSubmissionStatus else 0 end) as BSS29,
						max(case when " + ColumnNumberExpression + @" = 30 then css.BatchSubmissionStatus else 0 end) as BSS30,
						max(case when " + ColumnNumberExpression + @" = 31 then css.BatchSubmissionStatus else 0 end) as BSS31,
						max(case when " + ColumnNumberExpression + @" = 32 then css.BatchSubmissionStatus else 0 end) as BSS32,
						max(case when " + ColumnNumberExpression + @" = 33 then css.BatchSubmissionStatus else 0 end) as BSS33,
						max(case when " + ColumnNumberExpression + @" = 34 then css.BatchSubmissionStatus else 0 end) as BSS34,
						max(case when " + ColumnNumberExpression + @" = 35 then css.BatchSubmissionStatus else 0 end) as BSS35,");
			else
				sbSQLData.Append (@"						0 as BatchSubmissionStatus,
					0 as BSS1, 0 as BSS2, 0 as BSS3, 0 as BSS4, 0 as BSS5, 0 as BSS6, 0 as BSS7, 0 as BSS8, 0 as BSS9, 0 as BSS10,
					0 as BSS11, 0 as BSS12, 0 as BSS13, 0 as BSS14, 0 as BSS15, 0 as BSS16, 0 as BSS17, 0 as BSS18, 0 as BSS19, 0 as BSS20,
					0 as BSS21, 0 as BSS22, 0 as BSS23, 0 as BSS24, 0 as BSS25, 0 as BSS26, 0 as BSS27, 0 as BSS28, 0 as BSS29, 0 as BSS30,
					0 as BSS31, 0 as BSS32, 0 as BSS33, 0 as BSS34, 0 as BSS35,");
			if (blnCalculatedDiscounts)
				sbSQLData.Append (@"
						sum(Amount) as TotalSales,");
			else
				sbSQLData.Append (@"
						sum(case when css.Discount=0 then Amount else 0 end) as TotalSales,");
			sbSQLData.Append (@"
						sum(case when (css.Discount=0 or @DiscountFlag=1) then css.Quantity else 0 end) as Quantity,");
			if (blnCalculatedDiscounts)
				sbSQLData.Append (@"
						sum(DiscountAmount) as Discounts,");
			else
				sbSQLData.Append (@"
						sum(case when css.Discount<>0 then css.DiscountAmount else 0 end) as Discounts,");
			sbSQLData.Append (@"
						sum(case when css.Discount<>0 then css.DiscountQuantity else 0 end) as DiscountQuantity,");
			sbSQLData.Append (@"
						sum(css.Amount+css.DiscountAmount) AS NetSales,");
			sbSQLData.Append (@"
						sum(case when Discount=0 and MainTypeID=1 then css.Amount else 0 end) as RoyaltyFoodSales,
						sum(case when Discount=0 and MainTypeID=2 then css.Amount else 0 end) as RoyaltyBeverageSales,
						sum(case when Discount=0 and MainTypeID=3 then css.Amount else 0 end) as RoyaltyOtherSales,");
			if (blnCalculatedDiscounts)
				sbSQLData.Append (@"
						sum(DiscountAmount-RoyaltyExcludedDiscounts) as RoyaltyDiscounts," + "\n");
			else
				sbSQLData.Append (@"
						sum(case when Discount<>0 then css.DiscountAmount-RoyaltyExcludedDiscounts else 0 end) as RoyaltyDiscounts," + "\n");
			if (CostMode==2)
				sbSQLData.Append (@"						sum(css.Cost) as ProductCost," + "\n");
			if (DiscountFlag==1) {
				sbSQLData.Append (@"						sum(case when " + ColumnNumberExpression + @" = 1 then Amount+DiscountAmount else 0 end) as D1,
						sum(case when " + ColumnNumberExpression + @" = 2 then Amount+DiscountAmount else 0 end) as D2,
						sum(case when " + ColumnNumberExpression + @" = 3 then Amount+DiscountAmount else 0 end) as D3,
						sum(case when " + ColumnNumberExpression + @" = 4 then Amount+DiscountAmount else 0 end) as D4,
						sum(case when " + ColumnNumberExpression + @" = 5 then Amount+DiscountAmount else 0 end) as D5,
						sum(case when " + ColumnNumberExpression + @" = 6 then Amount+DiscountAmount else 0 end) as D6,
						sum(case when " + ColumnNumberExpression + @" = 7 then Amount+DiscountAmount else 0 end) as D7,
						sum(case when " + ColumnNumberExpression + @" = 8 then Amount+DiscountAmount else 0 end) as D8,
						sum(case when " + ColumnNumberExpression + @" = 9 then Amount+DiscountAmount else 0 end) as D9,
						sum(case when " + ColumnNumberExpression + @" = 10 then Amount+DiscountAmount else 0 end) as D10,
						sum(case when " + ColumnNumberExpression + @" = 11 then Amount+DiscountAmount else 0 end) as D11,
						sum(case when " + ColumnNumberExpression + @" = 12 then Amount+DiscountAmount else 0 end) as D12,
						sum(case when " + ColumnNumberExpression + @" = 13 then Amount+DiscountAmount else 0 end) as D13,
						sum(case when " + ColumnNumberExpression + @" = 14 then Amount+DiscountAmount else 0 end) as D14,
						sum(case when " + ColumnNumberExpression + @" = 15 then Amount+DiscountAmount else 0 end) as D15,
						sum(case when " + ColumnNumberExpression + @" = 16 then Amount+DiscountAmount else 0 end) as D16,
						sum(case when " + ColumnNumberExpression + @" = 17 then Amount+DiscountAmount else 0 end) as D17,
						sum(case when " + ColumnNumberExpression + @" = 18 then Amount+DiscountAmount else 0 end) as D18,
						sum(case when " + ColumnNumberExpression + @" = 19 then Amount+DiscountAmount else 0 end) as D19,
						sum(case when " + ColumnNumberExpression + @" = 20 then Amount+DiscountAmount else 0 end) as D20,
						sum(case when " + ColumnNumberExpression + @" = 21 then Amount+DiscountAmount else 0 end) as D21,
						sum(case when " + ColumnNumberExpression + @" = 22 then Amount+DiscountAmount else 0 end) as D22,
						sum(case when " + ColumnNumberExpression + @" = 23 then Amount+DiscountAmount else 0 end) as D23,
						sum(case when " + ColumnNumberExpression + @" = 24 then Amount+DiscountAmount else 0 end) as D24,
						sum(case when " + ColumnNumberExpression + @" = 25 then Amount+DiscountAmount else 0 end) as D25,
						sum(case when " + ColumnNumberExpression + @" = 26 then Amount+DiscountAmount else 0 end) as D26,
						sum(case when " + ColumnNumberExpression + @" = 27 then Amount+DiscountAmount else 0 end) as D27,
						sum(case when " + ColumnNumberExpression + @" = 28 then Amount+DiscountAmount else 0 end) as D28,
						sum(case when " + ColumnNumberExpression + @" = 29 then Amount+DiscountAmount else 0 end) as D29,
						sum(case when " + ColumnNumberExpression + @" = 30 then Amount+DiscountAmount else 0 end) as D30,
						sum(case when " + ColumnNumberExpression + @" = 31 then Amount+DiscountAmount else 0 end) as D31,
						sum(case when " + ColumnNumberExpression + @" = 32 then Amount+DiscountAmount else 0 end) as D32,
						sum(case when " + ColumnNumberExpression + @" = 33 then Amount+DiscountAmount else 0 end) as D33,
						sum(case when " + ColumnNumberExpression + @" = 34 then Amount+DiscountAmount else 0 end) as D34,
						sum(case when " + ColumnNumberExpression + @" = 35 then Amount+DiscountAmount else 0 end) as D35,
						0 as DS1, 0 as DS2, 0 as DS3, 0 as DS4, 0 as DS5, 0 as DS6, 0 as DS7, 0 as DS8,  0 as DS9,  0 as DS10,
							0 as DS11, 0 as DS12, 0 as DS13, 0 as DS14, 0 as DS15, 0 as DS16, 0 as DS17, 0 as DS18, 0 as DS19, 0 as DS20,
							0 as DS21, 0 as DS22, 0 as DS23, 0 as DS24, 0 as DS25, 0 as DS26, 0 as DS27, 0 as DS28, 0 as DS29, 0 as DS30,
							0 as DS31, 0 as DS32, 0 as DS33, 0 as DS34, 0 as DS35,
						0 as N1,  0 as N2,  0 as N3,  0 as N4,  0 as N5,  0 as N6,  0 as N7,  0 as N8,  0 as N9,  0 as N10,
							0 as N11, 0 as N12, 0 as N13, 0 as N14, 0 as N15, 0 as N16, 0 as N17, 0 as N18, 0 as N19, 0 as N20,
							0 as N21, 0 as N22, 0 as N23, 0 as N24, 0 as N25, 0 as N26, 0 as N27, 0 as N28, 0 as N29, 0 as N30,
							0 as N31, 0 as N32, 0 as N33, 0 as N34, 0 as N35," + "\n");
				}
			else {
				sbSQLData.Append (@"						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 1 then Amount else 0 end) as D1,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 2 then Amount else 0 end) as D2,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 3 then Amount else 0 end) as D3,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 4 then Amount else 0 end) as D4,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 5 then Amount else 0 end) as D5,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 6 then Amount else 0 end) as D6,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 7 then Amount else 0 end) as D7,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 8 then Amount else 0 end) as D8,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 9 then Amount else 0 end) as D9,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 10 then Amount else 0 end) as D10,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 11 then Amount else 0 end) as D11,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 12 then Amount else 0 end) as D12,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 13 then Amount else 0 end) as D13,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 14 then Amount else 0 end) as D14,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 15 then Amount else 0 end) as D15,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 16 then Amount else 0 end) as D16,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 17 then Amount else 0 end) as D17,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 18 then Amount else 0 end) as D18,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 19 then Amount else 0 end) as D19,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 20 then Amount else 0 end) as D20,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 21 then Amount else 0 end) as D21,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 22 then Amount else 0 end) as D22,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 23 then Amount else 0 end) as D23,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 24 then Amount else 0 end) as D24,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 25 then Amount else 0 end) as D25,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 26 then Amount else 0 end) as D26,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 27 then Amount else 0 end) as D27,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 28 then Amount else 0 end) as D28,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 29 then Amount else 0 end) as D29,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 30 then Amount else 0 end) as D30,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 31 then Amount else 0 end) as D31,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 32 then Amount else 0 end) as D32,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 33 then Amount else 0 end) as D33,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 34 then Amount else 0 end) as D34,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 35 then Amount else 0 end) as D35,");
			if (blnCalculatedDiscounts)
				sbSQLData.Append (@"
						sum(case when " + ColumnNumberExpression + @" = 1 then DiscountAmount else 0 end) as DS1,
						sum(case when " + ColumnNumberExpression + @" = 2 then DiscountAmount else 0 end) as DS2,
						sum(case when " + ColumnNumberExpression + @" = 3 then DiscountAmount else 0 end) as DS3,
						sum(case when " + ColumnNumberExpression + @" = 4 then DiscountAmount else 0 end) as DS4,
						sum(case when " + ColumnNumberExpression + @" = 5 then DiscountAmount else 0 end) as DS5,
						sum(case when " + ColumnNumberExpression + @" = 6 then DiscountAmount else 0 end) as DS6,
						sum(case when " + ColumnNumberExpression + @" = 7 then DiscountAmount else 0 end) as DS7,
						sum(case when " + ColumnNumberExpression + @" = 8 then DiscountAmount else 0 end) as DS8,
						sum(case when " + ColumnNumberExpression + @" = 9 then DiscountAmount else 0 end) as DS9,
						sum(case when " + ColumnNumberExpression + @" = 10 then DiscountAmount else 0 end) as DS10,
						sum(case when " + ColumnNumberExpression + @" = 11 then DiscountAmount else 0 end) as DS11,
						sum(case when " + ColumnNumberExpression + @" = 12 then DiscountAmount else 0 end) as DS12,
						sum(case when " + ColumnNumberExpression + @" = 13 then DiscountAmount else 0 end) as DS13,
						sum(case when " + ColumnNumberExpression + @" = 14 then DiscountAmount else 0 end) as DS14,
						sum(case when " + ColumnNumberExpression + @" = 15 then DiscountAmount else 0 end) as DS15,
						sum(case when " + ColumnNumberExpression + @" = 16 then DiscountAmount else 0 end) as DS16,
						sum(case when " + ColumnNumberExpression + @" = 17 then DiscountAmount else 0 end) as DS17,
						sum(case when " + ColumnNumberExpression + @" = 18 then DiscountAmount else 0 end) as DS18,
						sum(case when " + ColumnNumberExpression + @" = 19 then DiscountAmount else 0 end) as DS19,
						sum(case when " + ColumnNumberExpression + @" = 20 then DiscountAmount else 0 end) as DS20,
						sum(case when " + ColumnNumberExpression + @" = 21 then DiscountAmount else 0 end) as DS21,
						sum(case when " + ColumnNumberExpression + @" = 22 then DiscountAmount else 0 end) as DS22,
						sum(case when " + ColumnNumberExpression + @" = 23 then DiscountAmount else 0 end) as DS23,
						sum(case when " + ColumnNumberExpression + @" = 24 then DiscountAmount else 0 end) as DS24,
						sum(case when " + ColumnNumberExpression + @" = 25 then DiscountAmount else 0 end) as DS25,
						sum(case when " + ColumnNumberExpression + @" = 26 then DiscountAmount else 0 end) as DS26,
						sum(case when " + ColumnNumberExpression + @" = 27 then DiscountAmount else 0 end) as DS27,
						sum(case when " + ColumnNumberExpression + @" = 28 then DiscountAmount else 0 end) as DS28,
						sum(case when " + ColumnNumberExpression + @" = 29 then DiscountAmount else 0 end) as DS29,
						sum(case when " + ColumnNumberExpression + @" = 30 then DiscountAmount else 0 end) as DS30,
						sum(case when " + ColumnNumberExpression + @" = 31 then DiscountAmount else 0 end) as DS31,
						sum(case when " + ColumnNumberExpression + @" = 32 then DiscountAmount else 0 end) as DS32,
						sum(case when " + ColumnNumberExpression + @" = 33 then DiscountAmount else 0 end) as DS33,
						sum(case when " + ColumnNumberExpression + @" = 34 then DiscountAmount else 0 end) as DS34,
						sum(case when " + ColumnNumberExpression + @" = 35 then DiscountAmount else 0 end) as DS35,");
			else
				sbSQLData.Append (@"
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 1 then DiscountAmount else 0 end) as DS1,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 2 then DiscountAmount else 0 end) as DS2,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 3 then DiscountAmount else 0 end) as DS3,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 4 then DiscountAmount else 0 end) as DS4,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 5 then DiscountAmount else 0 end) as DS5,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 6 then DiscountAmount else 0 end) as DS6,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 7 then DiscountAmount else 0 end) as DS7,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 8 then DiscountAmount else 0 end) as DS8,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 9 then DiscountAmount else 0 end) as DS9,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 10 then DiscountAmount else 0 end) as DS10,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 11 then DiscountAmount else 0 end) as DS11,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 12 then DiscountAmount else 0 end) as DS12,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 13 then DiscountAmount else 0 end) as DS13,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 14 then DiscountAmount else 0 end) as DS14,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 15 then DiscountAmount else 0 end) as DS15,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 16 then DiscountAmount else 0 end) as DS16,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 17 then DiscountAmount else 0 end) as DS17,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 18 then DiscountAmount else 0 end) as DS18,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 19 then DiscountAmount else 0 end) as DS19,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 20 then DiscountAmount else 0 end) as DS20,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 21 then DiscountAmount else 0 end) as DS21,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 22 then DiscountAmount else 0 end) as DS22,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 23 then DiscountAmount else 0 end) as DS23,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 24 then DiscountAmount else 0 end) as DS24,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 25 then DiscountAmount else 0 end) as DS25,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 26 then DiscountAmount else 0 end) as DS26,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 27 then DiscountAmount else 0 end) as DS27,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 28 then DiscountAmount else 0 end) as DS28,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 29 then DiscountAmount else 0 end) as DS29,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 30 then DiscountAmount else 0 end) as DS30,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 31 then DiscountAmount else 0 end) as DS31,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 32 then DiscountAmount else 0 end) as DS32,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 33 then DiscountAmount else 0 end) as DS33,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 34 then DiscountAmount else 0 end) as DS34,
						sum(case when css.Discount<>0 and " + ColumnNumberExpression + @" = 35 then DiscountAmount else 0 end) as DS35,");
			sbSQLData.Append (@"
						sum(case when " + ColumnNumberExpression + @" = 1 then Amount+DiscountAmount else 0 end) as N1,
						sum(case when " + ColumnNumberExpression + @" = 2 then Amount+DiscountAmount else 0 end) as N2,
						sum(case when " + ColumnNumberExpression + @" = 3 then Amount+DiscountAmount else 0 end) as N3,
						sum(case when " + ColumnNumberExpression + @" = 4 then Amount+DiscountAmount else 0 end) as N4,
						sum(case when " + ColumnNumberExpression + @" = 5 then Amount+DiscountAmount else 0 end) as N5,
						sum(case when " + ColumnNumberExpression + @" = 6 then Amount+DiscountAmount else 0 end) as N6,
						sum(case when " + ColumnNumberExpression + @" = 7 then Amount+DiscountAmount else 0 end) as N7,
						sum(case when " + ColumnNumberExpression + @" = 8 then Amount+DiscountAmount else 0 end) as N8,
						sum(case when " + ColumnNumberExpression + @" = 9 then Amount+DiscountAmount else 0 end) as N9,
						sum(case when " + ColumnNumberExpression + @" = 10 then Amount+DiscountAmount else 0 end) as N10,
						sum(case when " + ColumnNumberExpression + @" = 11 then Amount+DiscountAmount else 0 end) as N11,
						sum(case when " + ColumnNumberExpression + @" = 12 then Amount+DiscountAmount else 0 end) as N12,
						sum(case when " + ColumnNumberExpression + @" = 13 then Amount+DiscountAmount else 0 end) as N13,
						sum(case when " + ColumnNumberExpression + @" = 14 then Amount+DiscountAmount else 0 end) as N14,
						sum(case when " + ColumnNumberExpression + @" = 15 then Amount+DiscountAmount else 0 end) as N15,
						sum(case when " + ColumnNumberExpression + @" = 16 then Amount+DiscountAmount else 0 end) as N16,
						sum(case when " + ColumnNumberExpression + @" = 17 then Amount+DiscountAmount else 0 end) as N17,
						sum(case when " + ColumnNumberExpression + @" = 18 then Amount+DiscountAmount else 0 end) as N18,
						sum(case when " + ColumnNumberExpression + @" = 19 then Amount+DiscountAmount else 0 end) as N19,
						sum(case when " + ColumnNumberExpression + @" = 20 then Amount+DiscountAmount else 0 end) as N20,
						sum(case when " + ColumnNumberExpression + @" = 21 then Amount+DiscountAmount else 0 end) as N21,
						sum(case when " + ColumnNumberExpression + @" = 22 then Amount+DiscountAmount else 0 end) as N22,
						sum(case when " + ColumnNumberExpression + @" = 23 then Amount+DiscountAmount else 0 end) as N23,
						sum(case when " + ColumnNumberExpression + @" = 24 then Amount+DiscountAmount else 0 end) as N24,
						sum(case when " + ColumnNumberExpression + @" = 25 then Amount+DiscountAmount else 0 end) as N25,
						sum(case when " + ColumnNumberExpression + @" = 26 then Amount+DiscountAmount else 0 end) as N26,
						sum(case when " + ColumnNumberExpression + @" = 27 then Amount+DiscountAmount else 0 end) as N27,
						sum(case when " + ColumnNumberExpression + @" = 28 then Amount+DiscountAmount else 0 end) as N28,
						sum(case when " + ColumnNumberExpression + @" = 29 then Amount+DiscountAmount else 0 end) as N29,
						sum(case when " + ColumnNumberExpression + @" = 30 then Amount+DiscountAmount else 0 end) as N30,
						sum(case when " + ColumnNumberExpression + @" = 31 then Amount+DiscountAmount else 0 end) as N31,
						sum(case when " + ColumnNumberExpression + @" = 32 then Amount+DiscountAmount else 0 end) as N32,
						sum(case when " + ColumnNumberExpression + @" = 33 then Amount+DiscountAmount else 0 end) as N33,
						sum(case when " + ColumnNumberExpression + @" = 34 then Amount+DiscountAmount else 0 end) as N34,
						sum(case when " + ColumnNumberExpression + @" = 35 then Amount+DiscountAmount else 0 end) as N35,");
				}
			sbSQLData.Append (@"
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 1 then Quantity else 0 end) as Q1,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 2 then Quantity else 0 end) as Q2,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 3 then Quantity else 0 end) as Q3,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 4 then Quantity else 0 end) as Q4,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 5 then Quantity else 0 end) as Q5,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 6 then Quantity else 0 end) as Q6,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 7 then Quantity else 0 end) as Q7,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 8 then Quantity else 0 end) as Q8,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 9 then Quantity else 0 end) as Q9,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 10 then Quantity else 0 end) as Q10,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 11 then Quantity else 0 end) as Q11,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 12 then Quantity else 0 end) as Q12,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 13 then Quantity else 0 end) as Q13,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 14 then Quantity else 0 end) as Q14,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 15 then Quantity else 0 end) as Q15,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 16 then Quantity else 0 end) as Q16,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 17 then Quantity else 0 end) as Q17,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 18 then Quantity else 0 end) as Q18,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 19 then Quantity else 0 end) as Q19,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 20 then Quantity else 0 end) as Q20,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 21 then Quantity else 0 end) as Q21,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 22 then Quantity else 0 end) as Q22,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 23 then Quantity else 0 end) as Q23,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 24 then Quantity else 0 end) as Q24,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 25 then Quantity else 0 end) as Q25,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 26 then Quantity else 0 end) as Q26,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 27 then Quantity else 0 end) as Q27,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 28 then Quantity else 0 end) as Q28,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 29 then Quantity else 0 end) as Q29,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 30 then Quantity else 0 end) as Q30,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 31 then Quantity else 0 end) as Q31,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 32 then Quantity else 0 end) as Q32,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 33 then Quantity else 0 end) as Q33,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 34 then Quantity else 0 end) as Q34,
						sum(case when (css.Discount=0 or @DiscountFlag=1) and " + ColumnNumberExpression + @" = 35 then Quantity else 0 end) as Q35,
						sum(RoyaltyExcludedDiscounts) as RoyaltyExcludedDiscounts");
			if (CostMode==1)
				sbSQLData.Append (@",
						case
							when isnull(mic.LastMenuItemCost,0)+isnull(mi.PlateCost,0)=0 and css.Discount=0
								then " + (TheoreticalCostPercentageFieldExists?"isnull(st.TheoreticalCostPercentage,0)*isnull(css.ItemPrice,0)/100":"0") + @"
							else isnull(mic.LastMenuItemCost,0)+isnull(mi.PlateCost,0)
						end as InventoryCost,
						case
							when isnull(mic.LastMenuItemCost,0)+isnull(mi.PlateCost,0)=0 and css.Discount=0
								then 1
							when isnull(mic.LastMenuItemCost,0)<>0 then 2
							when isnull(mi.PlateCost,0)<>0 then 4
							else 1
						end as InventoryCostSource");
			else if (CostMode==2)
				sbSQLData.Append (@",
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 1 then Cost else 0 end) as C1,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 2 then Cost else 0 end) as C2,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 3 then Cost else 0 end) as C3,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 4 then Cost else 0 end) as C4,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 5 then Cost else 0 end) as C5,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 6 then Cost else 0 end) as C6,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 7 then Cost else 0 end) as C7,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 8 then Cost else 0 end) as C8,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 9 then Cost else 0 end) as C9,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 10 then Cost else 0 end) as C10,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 11 then Cost else 0 end) as C11,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 12 then Cost else 0 end) as C12,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 13 then Cost else 0 end) as C13,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 14 then Cost else 0 end) as C14,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 15 then Cost else 0 end) as C15,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 16 then Cost else 0 end) as C16,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 17 then Cost else 0 end) as C17,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 18 then Cost else 0 end) as C18,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 19 then Cost else 0 end) as C19,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 20 then Cost else 0 end) as C20,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 21 then Cost else 0 end) as C21,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 22 then Cost else 0 end) as C22,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 23 then Cost else 0 end) as C23,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 24 then Cost else 0 end) as C24,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 25 then Cost else 0 end) as C25,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 26 then Cost else 0 end) as C26,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 27 then Cost else 0 end) as C27,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 28 then Cost else 0 end) as C28,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 29 then Cost else 0 end) as C29,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 30 then Cost else 0 end) as C30,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 31 then Cost else 0 end) as C31,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 32 then Cost else 0 end) as C32,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 33 then Cost else 0 end) as C33,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 34 then Cost else 0 end) as C34,
						sum(case when css.Discount=0 and " + ColumnNumberExpression + @" = 35 then Cost else 0 end) as C35
						");
			Boolean SelectDetail = DayPartFlag==1 || DayPartListCSV.Length>0 || DayHourFlag==1 || DayHourListCSV.Length>0
					|| FrequentDinerPlanFlag==1 || FrequentDinerPlanListCSV.Length>0 || FrequentDinerCustomerFlag==1
					|| FrequentDinerCustomerListCSV.Length>0 || StoreStaffFlag==1 || StoreStaffListCSV.Length>0
					|| CheckNumberFlag==1 || CheckNumberListCSV.Length>0 || DiscountListCSV.Length>0 || ColumnNumberExpressionRequiresDetailJoin
					|| ColumnOutputDisplayMode==ColumnOutputDisplayModes.DayHourNumber;
			if (SelectDetail) {
				sbSQLData.Append (@"
					from (
						select
							csstt.CompareIndex,
							csstt.BatchID,
							csstt.DayNumber,
							csstt.StoreID,
							csstt.ProfitCenterID,
							cssdtt.DayPartID,
							cssdtt.DayHourNumber,
							cssdtt.FrequentDinerPlanID,
							cssdtt.FrequentDinerCustomerID,
							cssdtt.StoreStaffID,
							cssdtt.CheckNumberID,
							csstt.MainTypeID,
							csstt.MainGroupID,
							csstt.SaleTypeID,
							csstt.PLU,
							csstt.Discount,
							csstt.Special,
							csstt.WeekNumber,
							csstt.WeekDayNumber,
							cssdtt.Amount as Amount,
							cssdtt.Quantity as Quantity,
							" + (CostMode==2?"csstt.ItemCost*cssdtt.Quantity":"null") + @" as Cost,
							csstt.SaleItemID,
							csstt.ItemPrice,
							" + (ShowBatchSubmissionStatus?"					csstt.BatchSubmissionStatus,\n":"")
								+ @"cssdtt.DiscountAmount, cssdtt.RoyaltyExcludedDiscounts,
							cssdtt.DiscountQuantity
						from (");
				SalesSelect(sbSQLData,7,SelectDetail,ShowBatchSubmissionStatus);
				sbSQLData.Append(@"
							) csstt 
						inner join (");
				SalesDetailSelect(sbSQLData,7);
				sbSQLData.Append(@"
							) cssdtt
						--on csstt.SalesKey=cssdtt.SalesKey
						on csstt.BatchID=cssdtt.BatchID and csstt.SaleItemID=cssdtt.SaleItemID
						) css" + "\n");
				}
			else {
				sbSQLData.Append (@"
					from (");
				SalesSelect(sbSQLData,6,SelectDetail,ShowBatchSubmissionStatus);
				sbSQLData.Append(@"
						) css" + "\n");
				}
			if (CostMode>0)
				sbSQLData.Append (@"					inner join" + "\n" + @"						tblStores" + "\n" + @"					on css.StoreID=tblStores.StoreID" + "\n");
			if (CostMode==1)
				sbSQLData.Append (@"					left outer join
						tblMenuItemsCost mic
					on mic.StoreID=css.StoreID and mic.MenuItemID=css.PLU and css.DayNumber between mic.StartDayNumber and mic.EndDayNumber
					left outer join
						tblMenuItems mi
					on mi.StoreID=css.StoreID and mi.PLU=css.PLU
					left outer join
						tblSaleTypes st
					on st.StoreID=mi.StoreID
						and st.SaleTypeID=mi.SaleTypeID" + "\n");
			bool isGroupBys = false;
			sbSQLData.Append(@"					group by
						case when css.DayNumber between @StartDayNumber and @EndDayNumber then 0 else 1 end");
			isGroupBys = true;
			if (isGroupBys)
				sbSQLData.Append ("," + "\n");
			else
				sbSQLData.Append (@"					group By" + "\n");
			sbSQLData.Append (@"						css.CompareIndex");
			isGroupBys = true;
			if (DayPartFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.DayPartID");
				isGroupBys = true;
				}
			if (DayHourFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.DayHourNumber");
				isGroupBys = true;
				}
			if (FrequentDinerPlanFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.FrequentDinerPlanID");
				isGroupBys = true;
				}
			if (FrequentDinerCustomerFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.FrequentDinerCustomerID");
				isGroupBys = true;
				}
			if (StoreStaffFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.StoreStaffID");
				isGroupBys = true;
				}
			if (CheckNumberFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.CheckNumberID");
				isGroupBys = true;
				}
			if (StoreGroupFlag==1 || StoreGroupListCSV.Length>0 || StoreFlag==1 || ProfitCenterFlag==1 || MainTypeFlag==1
					|| MainGroupFlag==1 || SaleTypeFlag==1 || DiscountFlag==1 || MenuItemFlag==1 || MenuItemPLUFlag==1
					|| SpecialFlag==1 || CostMode>0 || FrequentDinerPlanFlag==1 || FrequentDinerCustomerFlag==1
					|| StoreStaffFlag==1 || CheckNumberFlag==1 || DiscountListCSV.Length>0) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append ("\t\t\t\t\t\tcss.StoreID");
				isGroupBys = true;
				}
			if (ProfitCenterFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append ("\t\t\t\t\t\tcss.ProfitCenterID");
				isGroupBys = true;
				}
			if (MainTypeFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.MainTypeID");
				isGroupBys = true;
				}
			if (MainGroupFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.MainGroupID");
				isGroupBys = true;
				}
			if (SaleTypeFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.SaleTypeID");
				isGroupBys = true;
				}
			if (MenuItemFlag==1 || MenuItemPLUFlag==1 || CostMode==1 || CostMode==3) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.PLU");
				isGroupBys = true;
				}
			if (DiscountFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.Discount");
				isGroupBys = true;
				}
			if (SpecialFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.Special");
				isGroupBys = true;
				}
			if (WeekFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.WeekNumber");
				isGroupBys = true;
				}
			if (WeekDayFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.WeekDayNumber");
				isGroupBys = true;
				}
			if (DayFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						css.DayNumber");
				isGroupBys = true;
				}
			if (CostMode==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"					group By" + "\n");
				sbSQLData.Append (@"						case
							when isnull(mic.LastMenuItemCost,0)+isnull(mi.PlateCost,0)=0 and css.Discount=0
								then " + (TheoreticalCostPercentageFieldExists?"isnull(st.TheoreticalCostPercentage,0)*isnull(css.ItemPrice,0)/100":"0") + @"
							else isnull(mic.LastMenuItemCost,0)+isnull(mi.PlateCost,0)
						end,
						case
							when isnull(mic.LastMenuItemCost,0)+isnull(mi.PlateCost,0)=0 and css.Discount=0
								then 1
							when isnull(mic.LastMenuItemCost,0)<>0 then 2
							when isnull(mi.PlateCost,0)<>0 then 4
							else 1
						end");
				isGroupBys = true;
				}
			sbSQLData.Append ("\n");
			sbSQLData.Append (@"					) cs" + "\n");
			if (StoreGroupFlag==1 || StoreFlag==1 || ProfitCenterFlag==1 || SaleTypeFlag==1 || CostMode>0 || StoreGroupListCSV.Length>0 || StoreStaffFlag==1 || StoreStaffListCSV.Length>0 || ColumnNumberExpressionRequiresDetailJoin)
				sbSQLData.Append (@"				inner join 
				  tblStores
				on cs.StoreID=tblStores.StoreID
				left outer join (
					select css.StoreID, SUM(case when IncludeInDDReviewSalesReviewChargesColumn=1 then Amount else 0 end) as TotalSelectedChargesAmount1,
						SUM(case when IncludeInDDReviewSalesReviewChargesColumn=2 then Amount else 0 end) as TotalSelectedChargesAmount2,
						SUM(case when IncludeInDDReviewSalesReviewChargesColumn=0 then Amount else 0 end) as TotalSelectedChargesAmount0
					from tblCashAndSalesSales css
					inner join tblMenuItems mi on css.StoreID=mi.StoreID and css.PLU=mi.PLU
					where css.DayNumber between @StartDayNumber and @EndDayNumber
						and css.SaleTypeID=0
						and (@StoreFlag<>0 or @StoreGroupFlag<>0)
						--and IncludeInDDReviewSalesReviewChargesColumn<>0
					group by css.StoreID
					) cq
				on cq.StoreID=tblStores.StoreID" + "\n");
			else
				sbSQLData.Append (@"				cross join (
					select null as TotalSelectedChargesAmount1, null as TotalSelectedChargesAmount2, null as TotalSelectedChargesAmount0
					) cq" + "\n");
			if (StoreGroupFlag==1)
				sbSQLData.Append (@"				inner join
					tblStoreGroups
				on tblStores.StoreGroupID=tblStoreGroups.StoreGroupID" + "\n");
			if (ProfitCenterFlag==1) {
				sbSQLData.Append (@"				inner join");
				if (!IsMarauder && dv.Count>0) {
					sbSQLData.Append (@" (
					select
						StoreID, ProfitCenterID, ProfitCenter, ProfitCenterGroup
					from tblProfitCenters
					union all
					select
						StoreID, ProfitCenterItemID as ProfitCenterID, PseudoProfitCenter as ProfitCenter,
						null as ProfitCenterGroup
					from tblStoresPseudoProfitCenters
					) ");
					}
				else
					sbSQLData.Append ("\t\t\t\t\t");
				sbSQLData.Append (@"tblProfitCenters
				on tblProfitCenters.StoreID=cs.StoreID and tblProfitCenters.ProfitCenterID=cs.ProfitCenterID" + "\n");
				}
			if (DayPartFlag==1) {
				sbSQLData.Append (@"				inner join
  				@DayPartsTable dpt
				on cs.DayPartID = dpt.DayPartID" + "\n");
				}
			if (DayHourFlag==1) {
				sbSQLData.Append (@"				inner join
  				@DayHourTable dht
				on cs.DayHourNumber = dht.DayHourNumber" + "\n");
				}
			if (FrequentDinerPlanFlag==1)
				sbSQLData.Append (@"				left outer join
  				tblFrequentDinerPlans
				on tblFrequentDinerPlans.StoreID=cs.StoreID and tblFrequentDinerPlans.FrequentDinerPlanID = cs.FrequentDinerPlanID" + "\n");
			if (FrequentDinerCustomerFlag==1)
				sbSQLData.Append (@"				left outer join
  				tblFrequentDinerCustomers
				on tblFrequentDinerCustomers.StoreID=cs.StoreID and tblFrequentDinerCustomers.FrequentDinerCustomerID = cs.FrequentDinerCustomerID" + "\n");
			if (StoreStaffFlag==1)
				sbSQLData.Append (@"				left outer join
  				tblStaff
				on tblStaff.StoreID=cs.StoreID and tblStaff.StoreStaffID = cs.StoreStaffID" + "\n");
			if (MainTypeFlag==1)
				sbSQLData.Append (@"				inner join
  				tblMainTypes mt
				on mt.MainTypeID = cs.MainTypeID" + "\n");
			if (WeekDayFlag==1)
				sbSQLData.Append (@"				inner join
  				@tblWeekDays wd
				on wd.WeekDayNumber = cs.WeekDayNumber" + "\n");
			if (MainGroupFlag==1) {
				sbSQLData.Append ("				left outer join ");
				sbSQLData.Append ((strMainGroupJoin.Length>0?strMainGroupJoin:"\n\t\t\t\t\ttblMainGroups") + @" mg
				on mg.StoreID=cs.StoreID and mg.MainGroupID=cs.MainGroupID" + "\n");
				}
			if (SaleTypeFlag==1) {
				sbSQLData.Append (@"				left outer join ");
  			sbSQLData.Append ((strSaleTypeJoin.Length>0?strSaleTypeJoin:"\n\t\t\t\t\ttblSaleTypes") + @" st
				on st.StoreID=cs.StoreID and st.SaleTypeID=cs.SaleTypeID" + "\n");
				}
			if (MenuItemFlag==1 || MenuItemPLUFlag==1 || CostMode==1 || CostMode==3) {
				sbSQLData.Append (@"				left outer join ");
				sbSQLData.Append ((strMenuItemJoin.Length>0?strMenuItemJoin:"\n\t\t\t\t\ttblMenuItems") + @" mi
				on mi.StoreID=cs.StoreID and mi.PLU=cs.MenuItemID" + "\n");
				}
			if (DiscountFlag==1) {
				sbSQLData.Append (@"				inner join ");
				if (strDiscountJoin.Length>0)
					sbSQLData.Append (strDiscountJoin);
				else
					sbSQLData.Append (@"(
					select StoreID, DiscountID, Discount, DiscountGroup from tblDiscounts
					union all
					select StoreID, -ReceiptTypeID as DiscountID, ReceiptType as Discount, null as DiscountGroup from tblReceiptTypes where ReceiptPostedAsDiscount=1
					union all
					select StoreID, 0, '<Sales>', null from tblStores
					)");
				sbSQLData.Append(@" dsc
				on dsc.StoreID=cs.StoreID and dsc.DiscountID=cs.DiscountID" + "\n");
				}
			if (IncludeVoids) {
				sbSQLData.Append (@"				left outer join (
					select
						StoreID,
						case when vrq.DayNumber between @StartDayNumber and @EndDayNumber then 0 else 1 end as CompareIndex,
						sum(vrq.VoidAmount) as TotalVoids, sum(RefundAmount) as TotalRefunds,
						sum(case when vrq.DayNumber%7+1 = 1 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids1,
						sum(case when vrq.DayNumber%7+1 = 2 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids2,
						sum(case when vrq.DayNumber%7+1 = 3 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids3,
						sum(case when vrq.DayNumber%7+1 = 4 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids4,
						sum(case when vrq.DayNumber%7+1 = 5 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids5,
						sum(case when vrq.DayNumber%7+1 = 6 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids6,
						sum(case when vrq.DayNumber%7+1 = 7 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids7,
						sum(case when vrq.DayNumber%7+1 = 8 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids8,
						sum(case when vrq.DayNumber%7+1 = 9 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids9,
						sum(case when vrq.DayNumber%7+1 = 10 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids10,
						sum(case when vrq.DayNumber%7+1 = 11 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids11,
						sum(case when vrq.DayNumber%7+1 = 12 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids12,
						sum(case when vrq.DayNumber%7+1 = 13 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids13,
						sum(case when vrq.DayNumber%7+1 = 14 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids14,
						sum(case when vrq.DayNumber%7+1 = 15 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids15,
						sum(case when vrq.DayNumber%7+1 = 16 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids16,
						sum(case when vrq.DayNumber%7+1 = 17 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids17,
						sum(case when vrq.DayNumber%7+1 = 18 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids18,
						sum(case when vrq.DayNumber%7+1 = 19 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids19,
						sum(case when vrq.DayNumber%7+1 = 20 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids20,
						sum(case when vrq.DayNumber%7+1 = 21 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids21,
						sum(case when vrq.DayNumber%7+1 = 22 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids22,
						sum(case when vrq.DayNumber%7+1 = 23 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids23,
						sum(case when vrq.DayNumber%7+1 = 24 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids24,
						sum(case when vrq.DayNumber%7+1 = 25 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids25,
						sum(case when vrq.DayNumber%7+1 = 26 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids26,
						sum(case when vrq.DayNumber%7+1 = 27 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids27,
						sum(case when vrq.DayNumber%7+1 = 28 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids28,
						sum(case when vrq.DayNumber%7+1 = 29 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids29,
						sum(case when vrq.DayNumber%7+1 = 30 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids30,
						sum(case when vrq.DayNumber%7+1 = 31 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids31,
						sum(case when vrq.DayNumber%7+1 = 32 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids32,
						sum(case when vrq.DayNumber%7+1 = 33 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids33,
						sum(case when vrq.DayNumber%7+1 = 34 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids34,
						sum(case when vrq.DayNumber%7+1 = 35 then vrq.VoidAmount+vrq.RefundAmount else 0 end) as Voids35
					from (
						select DayNumber, Amount as VoidAmount, 0 as RefundAmount, StoreID
						from tblCashAndSalesVoids csv
						where (csv.DayNumber between @StartDayNumber and @EndDayNumber or
							csv.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
						union all
						select cssd.DayNumber, 0 as VoidAmount, -cssd.Amount as RefundAmount, cssd.StoreID
						from tblCashAndSalesSalesDetail cssd inner join tblCashAndSalesSales css
						on css.BatchID=cssd.BatchID and css.SaleItemID=cssd.SaleItemID
						where (css.DayNumber between @StartDayNumber and @EndDayNumber or css.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
							and (cssd.DayNumber between @StartDayNumber and @EndDayNumber or cssd.DayNumber between @CompareStartDayNumber and @CompareEndDayNumber)
							and css.Discount=0 and cssd.Amount<0
						) vrq
					group by vrq.StoreID, case when vrq.DayNumber between @StartDayNumber and @EndDayNumber then 0 else 1 end
					) voids
				on voids.StoreID=cs.StoreID
					and voids.CompareIndex=cs.CompareIndex" + "\n");
				}
			string strOuterWhere = "";
			if (CostMode==1)
				strOuterWhere += @"cs.InventoryCost<>0";
			if (StoreGroupListCSV.Length>0) {
				if (strOuterWhere.Length>0)
					strOuterWhere += " and ";
				strOuterWhere += @"(tblStores.StoreGroupID in (" + StoreGroupListCSV + "))";
				}
			if (strOuterWhere.Length>0)
				sbSQLData.Append ("				where " + strOuterWhere + "\n");
			isGroupBys = false;
			if (isGroupBys)
				sbSQLData.Append ("," + "\n");
			else
				sbSQLData.Append (@"				group By" + "\n");
			sbSQLData.Append   (@"					cs.CompareIndex");
			//if (IncludeVoids)
			//	sbSQLData.Append(@",
			//		isnull(VOIDS1,0), isnull(VOIDS2,0), isnull(VOIDS3,0), isnull(VOIDS4,0), isnull(VOIDS5,0), isnull(VOIDS6,0), isnull(VOIDS7,0), isnull(VOIDS8,0), isnull(VOIDS9,0), isnull(VOIDS10,0),
			//		isnull(VOIDS11,0), isnull(VOIDS12,0), isnull(VOIDS13,0), isnull(VOIDS14,0), isnull(VOIDS15,0), isnull(VOIDS16,0), isnull(VOIDS17,0), isnull(VOIDS18,0), isnull(VOIDS19,0), isnull(VOIDS20,0),
			//		isnull(VOIDS21,0), isnull(VOIDS22,0), isnull(VOIDS23,0), isnull(VOIDS24,0), isnull(VOIDS25,0), isnull(VOIDS26,0), isnull(VOIDS27,0), isnull(VOIDS28,0), isnull(VOIDS29,0), isnull(VOIDS30,0),
			//		isnull(VOIDS31,0), isnull(VOIDS32,0), isnull(VOIDS33,0), isnull(VOIDS34,0), isnull(VOIDS35,0)");
			isGroupBys = true;
			if (DayPartFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.DayPartID, dpt.DayPart");
				isGroupBys = true;
				}
			if (DayHourFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.DayHourNumber, dht.DayHourName");
				isGroupBys = true;
				}
			if (FrequentDinerPlanFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.FrequentDinerPlanID, tblFrequentDinerPlans.FrequentDinerPlan");
				isGroupBys = true;
				}
			if (FrequentDinerCustomerFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.FrequentDinerCustomerID, tblFrequentDinerCustomers.LastName, tblFrequentDinerCustomers.FirstName");
				isGroupBys = true;
				}
			if (StoreStaffFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.StoreStaffID, isnull(ltrim(rtrim(tblStaff.LastName))+', ','')+isnull(ltrim(rtrim(tblStaff.FirstName)),'')");
				isGroupBys = true;
				}
			if (CheckNumberFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.CheckNumberID, cast(floor(cs.CheckNumberID/10000000000) as varchar(10)) + '-' + cast(floor(cs.CheckNumberID/100)%1000000 as varchar(10)) + '-' + cast(cs.CheckNumberID%100 as varchar(9))");
				isGroupBys = true;
				}
			if (StoreGroupFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					tblStores.StoreGroupID, tblStoreGroups.StoreGroupName, tblStoreGroups.DisplayOrder");
				isGroupBys = true;
				}
			if (StoreFlag==1 || ProfitCenterFlag==1 || StoreStaffFlag==1 || CostMode==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"				  cs.StoreID, tblStores.StoreNumber, isnull(tblStores.StoreReportName, tblStores.StoreName), tblStores.StoreGCode");
				isGroupBys = true;
				}
			if (ProfitCenterFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.ProfitCenterID, case when tblProfitCenters.ProfitCenterGroup is null then tblProfitCenters.ProfitCenter else tblProfitCenters.ProfitCenterGroup end");
				isGroupBys = true;
				}
			if (MainTypeFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.MainTypeID, mt.Description, mt.DisplayOrder");
				isGroupBys = true;
				}
			if (MainGroupFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.MainGroupID, mg.MainGroup");
				isGroupBys = true;
				}
			if (SaleTypeFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.SaleTypeID, case when st.SaleTypeGroup is null then st.SaleType else st.SaleTypeGroup end");
				isGroupBys = true;
				}
			if (MenuItemFlag==1 || MenuItemPLUFlag==1 || CostMode==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.MenuItemID, case when cs.MenuItemID=0 then '&lt;Special Item&gt;' when cs.MenuItemID>10000000 then '&lt;Service Charge&gt;' when mi.Description is null then '&lt;PLU: '+cast(cs.MenuItemID as varchar(12))+'&gt;' else mi.Description end");
				if (MenuItemFlag==1)
					sbSQLData.Append (", mi.RegularPrice");
				if (CostMode==1)
					sbSQLData.Append(@", cs.InventoryCost");
				isGroupBys = true;
				}
			if (MenuItemPLUFlag==1 && MenuItemFlag==0) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.MenuItemID");
				if (CostMode==1)
					sbSQLData.Append(@", cs.InventoryCost");
				isGroupBys = true;
				}
			if (DiscountFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.DiscountID, dsc.Discount");
				isGroupBys = true;
				}
			if (SpecialFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.Special");
				isGroupBys = true;
				}
			if (WeekFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.WeekNumber");
				isGroupBys = true;
				}
			if (WeekDayFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.WeekDayNumber, wd.WeekDayName");
				isGroupBys = true;
				}
			if (DayFlag==1) {
				if (isGroupBys)
					sbSQLData.Append ("," + "\n");
				else
					sbSQLData.Append (@"				group By" + "\n");
				sbSQLData.Append (@"					cs.DayNumber");
				isGroupBys = true;
				}
			sbSQLData.Append ("\n");
			if (CostMode>0)
				sbSQLData.Append (@"				) ct
			group by
				CompareIndex,
				DayPartID,
				DayPart,
				DayPartSort,
				DayHourNumber,
				DayHourName,
				FrequentDinerPlanID,
				FrequentDinerPlan,
				FrequentDinerCustomerID,
				FrequentDinerCustomer,
				StoreStaffID,
				StoreStaff,
				CheckNumberID,
				CheckNumber,
				StoreGroupID,
				StoreGroup,
				StoreGroupSort,
				StoreID,
				StoreNumber,
				StoreName,
				StoreGCode,
				ProfitCenterID,                         
				ProfitCenter,
				MainTypeID, 
				MainType,
				MainGroupID,
				MainGroup,
				SaleTypeID,
				SaleType,
				MenuItemID,
				MenuItem,
				MenuItemPLU,
				DiscountID,
				Discount,
				Special,
				WeekNumber,
				WeekName,
				WeekDayNumber,
				WeekDayName,
				DayNumber,
				DayName,
				MenuItemPrice,
				VOIDS1, VOIDS2, VOIDS3, VOIDS4, VOIDS5, VOIDS6, VOIDS7, VOIDS8, VOIDS9, VOIDS10,
					VOIDS11, VOIDS12, VOIDS13, VOIDS14, VOIDS15, VOIDS16, VOIDS17, VOIDS18, VOIDS19, VOIDS20,
					VOIDS21, VOIDS22, VOIDS23, VOIDS24, VOIDS25, VOIDS26, VOIDS27, VOIDS28, VOIDS29, VOIDS30,
					VOIDS31, VOIDS32, VOIDS33, VOIDS34, VOIDS35" + "\n\n");
			sbSQLData.Append (@"
				declare @header_table table (
					RowID int identity (0,1) not null,
					CompareIndex smallint,
					DayPartID int,
					DayPart varchar(50),
					DayPartSort varchar(6),
					DayHourNumber smallint,
					DayHourName varchar(50),
					FrequentDinerPlanID smallint,
					FrequentDinerPlan varchar(30),
					FrequentDinerCustomerID int,
					FrequentDinerCustomer varchar(42),
					StoreStaffID int,
					StoreStaff varchar(100),
					CheckNumberID bigint,
					CheckNumber varchar(20),
					StoreGroupID int,
					StoreGroup varchar(50),
					StoreGroupSort varchar(6),
					StoreID int,
					StoreNumber int,
					StoreName varchar(50),
					StoreGCode varchar(30),
					ProfitCenterID varchar(850),
					ProfitCenter varchar(50),
					MainTypeID int, 
					MainType varchar(50),
					MainGroupID int,
					MainGroup varchar(50),
					SaleTypeID varchar(770),
					SaleType varchar(50),
					MenuItemID varchar(2920),
					MenuItemRow bit,
					MenuItem varchar(50),
					MenuItemPLU varchar(16),
					DiscountID varchar(720),
					DiscountRow bit,
					Discount varchar(50),
					Special int,
					WeekNumber int,
					WeekName varchar(50),
					WeekDayNumber int,
					WeekDayName varchar(50),
					DayNumber int,
					DayName varchar(50),
					BatchSubmissionStatus int,
					BSS1 money, BSS2 money, BSS3 money, BSS4 money, BSS5 money, BSS6 money, BSS7 money, BSS8 money, BSS9 money, BSS10 money, 
						BSS11 money, BSS12 money, BSS13 money, BSS14 money, BSS15 money, BSS16 money, BSS17 money, BSS18 money, BSS19 money, BSS20 money, 
						BSS21 money, BSS22 money, BSS23 money, BSS24 money, BSS25 money, BSS26 money, BSS27 money, BSS28 money, BSS29 money, BSS30 money, 
						BSS31 money, BSS32 money, BSS33 money, BSS34 money, BSS35 money, 
					D1 money, D2 money, D3 money, D4 money, D5 money, D6 money, D7 money, D8 money, D9 money, D10 money, 
						D11 money, D12 money, D13 money, D14 money, D15 money, D16 money, D17 money, D18 money, D19 money, D20 money, 
						D21 money, D22 money, D23 money, D24 money, D25 money, D26 money, D27 money, D28 money, D29 money, D30 money, 
						D31 money, D32 money, D33 money, D34 money, D35 money, 
					DS1 money, DS2 money, DS3 money, DS4 money, DS5 money, DS6 money, DS7 money, DS8 money, DS9 money, DS10 money, 
						DS11 money, DS12 money, DS13 money, DS14 money, DS15 money, DS16 money, DS17 money, DS18 money, DS19 money, DS20 money, 
						DS21 money, DS22 money, DS23 money, DS24 money, DS25 money, DS26 money, DS27 money, DS28 money, DS29 money, DS30 money, 
						DS31 money, DS32 money, DS33 money, DS34 money, DS35 money,
					N1 money, N2 money, N3 money, N4 money, N5 money, N6 money, N7 money, N8 money, N9 money, N10 money, 
						N11 money, N12 money, N13 money, N14 money, N15 money, N16 money, N17 money, N18 money, N19 money, N20 money, 
						N21 money, N22 money, N23 money, N24 money, N25 money, N26 money, N27 money, N28 money, N29 money, N30 money, 
						N31 money, N32 money, N33 money, N34 money, N35 money, 
					Q1 money, Q2 money, Q3 money, Q4 money, Q5 money, Q6 money, Q7 money, Q8 money, Q9 money, Q10 money, 
						Q11 money, Q12 money, Q13 money, Q14 money, Q15 money, Q16 money, Q17 money, Q18 money, Q19 money, Q20 money, 
						Q21 money, Q22 money, Q23 money, Q24 money, Q25 money, Q26 money, Q27 money, Q28 money, Q29 money, Q30 money, 
						Q31 money, Q32 money, Q33 money, Q34 money, Q35 money, 
					VOIDS1 money, VOIDS2 money, VOIDS3 money, VOIDS4 money, VOIDS5 money, VOIDS6 money, VOIDS7 money, VOIDS8 money, VOIDS9 money, VOIDS10 money, 
						VOIDS11 money, VOIDS12 money, VOIDS13 money, VOIDS14 money, VOIDS15 money, VOIDS16 money, VOIDS17 money, VOIDS18 money, VOIDS19 money, VOIDS20 money, 
						VOIDS21 money, VOIDS22 money, VOIDS23 money, VOIDS24 money, VOIDS25 money, VOIDS26 money, VOIDS27 money, VOIDS28 money, VOIDS29 money, VOIDS30 money, 
						VOIDS31 money, VOIDS32 money, VOIDS33 money, VOIDS34 money, VOIDS35 money");
			if (CostMode>0)
				sbSQLData.Append (@",
					C1 money, C2 money, C3 money, C4 money, C5 money, C6 money, C7 money, C8 money, C9 money, C10 money, 
						C11 money, C12 money, C13 money, C14 money, C15 money, C16 money, C17 money, C18 money, C19 money, C20 money, 
						C21 money, C22 money, C23 money, C24 money, C25 money, C26 money, C27 money, C28 money, C29 money, C30 money, 
						C31 money, C32 money, C33 money, C34 money, C35 money");
			sbSQLData.Append (@",
					TotalSales money,
					Discounts money,
					NetSales money,
					Quantity money,
					TotalVoids money,
					TotalRefunds money,
					TotalSelectedCharges1 money,
					TotalSelectedCharges2 money,
					TotalSelectedCharges0 money,");
			if (CostMode==0)
				sbSQLData.Append (@"
						RoyaltyFoodSales money,
						RoyaltyBeverageSales money,
						RoyaltyOtherSales money,
						RoyaltyDiscounts money,
						RoyaltyExcludedDiscounts money,");
			sbSQLData.Append (@"
					RoyaltyColumn1Amount money,
					RoyaltyColumn1Label varchar(100),
					RoyaltyColumn1DisplayAttribute int default(0),
					RoyaltyColumn2Amount money,
					RoyaltyColumn2Label varchar(100),
					RoyaltyColumn2DisplayAttribute int default(0),
					RoyaltyColumn3Amount money,
					RoyaltyColumn3Label varchar(100),
					RoyaltyColumn3DisplayAttribute int default(0),
					RoyaltyColumn4Amount money,
					RoyaltyColumn4Label varchar(100),
					RoyaltyColumn4DisplayAttribute int default(0)");
			if (CostMode==1||CostMode==2)
				sbSQLData.Append (@",
					TotalQuantity money,
					CostGrossPercentage money,
					DiscountsPercentage money,
					CostNetPercentage money,
					AdjustedProductCost money,
					AdjustedProductCostPercentage money,
					AdjustedNetProfit money,
					AdjustedNetProfitPercentage money,
					MenuItemPrice money, 
					ProductCost money,
					UnitCost money,
					UnitCostSource int,
					PercentOfTotalItems decimal(18,12),
					PercentOfMenuMix decimal(18,12),
					GrossProfitPerPlate money,
					ItemSales money,
					ItemCost money,
					ItemGrossProfit money,
					PlateGrossProfit money,
					PlateAverageProfit money,
					MenuItemProfitabilityRatingID int,
					MenuItemProfitabilityRating varchar(50),
					MenuItemProfitabilityRatingSort money,
					ReportStartDate datetime,
					ReportEndDate datetime");
			sbSQLData.Append ("\n\t\t\t\t\t)\n\n");
			sbSQLData.Append (@"				insert into @header_table
				select" + "\n");
			if (OffsetDays!=System.Int32.MinValue)
				sbSQLData.Append (@"
					CompareIndex,");
			else
				sbSQLData.Append (@"
					null as CompareIndex,");
			if (DayPartFlag==1)
				sbSQLData.Append (@"
					DayPartID,
					DayPart,
					DayPartSort,");
			else
				sbSQLData.Append (@"
					null as DayPartID,
					null as DayPart,
					null as DayPartSort,");
			if (DayHourFlag==1)
				sbSQLData.Append (@"
					DayHourNumber,
					DayHourName,");
			else
				sbSQLData.Append (@"
					null as DayHourNumber,
					null as DayHourName,");
			if (FrequentDinerPlanFlag==1)
				sbSQLData.Append (@"
					FrequentDinerPlanID,
					FrequentDinerPlan,");
			else
				sbSQLData.Append (@"
					null as FrequentDinerPlanID,
					null as FrequentDInerPlan,");
			if (FrequentDinerCustomerFlag==1)
				sbSQLData.Append (@"
					FrequentDinerCustomerID,
					FrequentDinerCustomer,");
			else
				sbSQLData.Append (@"
					null as FrequentDinerCustomerID,
					null as FrequentDinerCustomer,");
			if (StoreStaffFlag==1)
				if (StoreFlag==1)
					sbSQLData.Append (@"
						StoreStaffID,
						StoreStaff,");
				else
					sbSQLData.Append (@"
						null as StoreStaffID,
						StoreStaff,");
			else
				sbSQLData.Append (@"
					null as StoreStaffID,
					null as StoreStaff,");
			if (CheckNumberFlag==1)
				sbSQLData.Append (@"
					CheckNumberID,
					CheckNumber,");
			else
				sbSQLData.Append (@"
					null as CheckNumberID,
					null as CheckNumber,");
			sbSQLData.Append (@"
					StoreGroupID,
					StoreGroup,
					StoreGroupSort,");
			if (StoreFlag==1)
				sbSQLData.Append(@"
					StoreID,
					StoreNumber,
					StoreName,
					StoreGCode,");
			else
				sbSQLData.Append(@"
					null as StoreID,
					null as StoreNumber,
					null as StoreName,
					null as StoreGCode,");
			sbSQLData.Append (@"
					null as ProfitCenterID,
					ProfitCenter,
					MainTypeID, 
					MainType,
					null as MainGroupID,
					MainGroup,
					null as SaleTypeID,
					SaleType,
					" + (MenuItemPLUFlag==1?"MenuItemID":"null as MenuItemID") + @",
					case when MenuItemID=0 or MenuItemID>10000000 or left(MenuItem,9)='&lt;PLU: ' and right(MenuItem,4)='&gt;' then 0 else 1 end as MenuItemRow,
					MenuItem,
					" + (MenuItemPLUFlag==1?"MenuItemPLU":"null as MenuItemPLU") + @",
					null as DiscountID,
					case when DiscountID=0 then 0 else 1 end as DiscountRow,
					Discount,
					Special,
					WeekNumber,
					WeekName,
					WeekDayNumber,
					WeekDayName,
					DayNumber,
					DayName,
					max(BatchSubmissionStatus) as BatchSubmissionStatus,
					max(BSS1) as BSS1, max(BSS2) as BSS2, max(BSS3) as BSS3, max(BSS4) as BSS4, max(BSS5) as BSS5, max(BSS6) as BSS6, max(BSS7) as BSS7, max(BSS8) as BSS8, max(BSS9) as BSS9, max(BSS10) as BSS10,
						max(BSS11) as BSS11, max(BSS12) as BSS12, max(BSS13) as BSS13, max(BSS14) as BSS14, max(BSS15) as BSS15, max(BSS16) as BSS16, max(BSS17) as BSS17, max(BSS18) as BSS18, max(BSS19) as BSS19, max(BSS20) as BSS20,
						max(BSS21) as BSS21, max(BSS22) as BSS22, max(BSS23) as BSS23, max(BSS24) as BSS24, max(BSS25) as BSS25, max(BSS26) as BSS26, max(BSS27) as BSS27, max(BSS28) as BSS28, max(BSS29) as BSS29, max(BSS30) as BSS30,
						max(BSS23) as BSS31, max(BSS32) as BSS32, max(BSS33) as BSS33, max(BSS34) as BSS34, max(BSS35) as BSS35,
					sum(D1) as D1, sum(D2) as D2, sum(D3) as D3, sum(D4) as D4, sum(D5) as D5, sum(D6) as D6, sum(D7) as D7, sum(D8) as D8, sum(D9) as D9, sum(D10) as D10,
						sum(D11) as D11, sum(D12) as D12, sum(D13) as D13, sum(D14) as D14, sum(D15) as D15, sum(D16) as D16, sum(D17) as D17, sum(D18) as D18, sum(D19) as D19, sum(D20) as D20,
						sum(D21) as D21, sum(D22) as D22, sum(D23) as D23, sum(D24) as D24, sum(D25) as D25, sum(D26) as D26, sum(D27) as D27, sum(D28) as D28, sum(D29) as D29, sum(D30) as D30,
						sum(D23) as D31, sum(D32) as D32, sum(D33) as D33, sum(D34) as D34, sum(D35) as D35,
					sum(DS1) as DS1, sum(DS2) as DS2, sum(DS3) as DS3, sum(DS4) as DS4, sum(DS5) as DS5, sum(DS6) as DS6, sum(DS7) as DS7, sum(DS8) as DS8, sum(DS9) as DS9, sum(DS10) as DS10,
						sum(DS11) as DS11, sum(DS12) as DS12, sum(DS13) as DS13, sum(DS14) as DS14, sum(DS15) as DS15, sum(DS16) as DS16, sum(DS17) as DS17, sum(DS18) as DS18, sum(DS19) as DS19, sum(DS20) as DS20,
						sum(DS21) as DS21, sum(DS22) as DS22, sum(DS23) as DS23, sum(DS24) as DS24, sum(DS25) as DS25, sum(DS26) as DS26, sum(DS27) as DS27, sum(DS28) as DS28, sum(DS29) as DS29, sum(DS30) as DS30,
						sum(DS23) as DS31, sum(DS32) as DS32, sum(DS33) as DS33, sum(DS34) as DS34, sum(DS35) as DS35,
					sum(N1) as N1, sum(N2) as N2, sum(N3) as N3, sum(N4) as N4, sum(N5) as N5, sum(N6) as N6, sum(N7) as N7, sum(N8) as N8, sum(N9) as N9, sum(N10) as N10,
						sum(N11) as N11, sum(N12) as N12, sum(N13) as N13, sum(N14) as N14, sum(N15) as N15, sum(N16) as N16, sum(N17) as N17, sum(N18) as N18, sum(N19) as N19, sum(N20) as N20,
						sum(N21) as N21, sum(N22) as N22, sum(N23) as N23, sum(N24) as N24, sum(N25) as N25, sum(N26) as N26, sum(N27) as N27, sum(N28) as N28, sum(N29) as N29, sum(N30) as N30,
						sum(N23) as N31, sum(N32) as N32, sum(N33) as N33, sum(N34) as N34, sum(N35) as N35,
					sum(Q1) as Q1, sum(Q2) as Q2, sum(Q3) as Q3, sum(Q4) as Q4, sum(Q5) as Q5, sum(Q6) as Q6, sum(Q7) as Q7,  sum(Q8) as Q8, sum(Q9) as Q9, sum(Q10) as Q10,
						sum(Q11) as Q11, sum(Q12) as Q12, sum(Q13) as Q13, sum(Q14) as Q14, sum(Q15) as Q15, sum(Q16) as Q16, sum(Q17) as Q17, sum(Q18) as Q18, sum(Q19) as Q19, sum(Q20) as Q20,
						sum(Q21) as Q21, sum(Q22) as Q22, sum(Q23) as Q23, sum(Q24) as Q24, sum(Q25) as Q25, sum(Q26) as Q26, sum(Q27) as Q27, sum(Q28) as Q28, sum(Q29) as Q29, sum(Q30) as Q30,
						sum(Q23) as Q31, sum(Q32) as Q32, sum(Q33) as Q33, sum(Q34) as Q34, sum(Q35) as Q35,
					VOIDS1, VOIDS2, VOIDS3, VOIDS4, VOIDS5, VOIDS6, VOIDS7, VOIDS8, VOIDS9, VOIDS10,
					VOIDS11, VOIDS12, VOIDS13, VOIDS14, VOIDS15, VOIDS16, VOIDS17, VOIDS18, VOIDS19, VOIDS20,
					VOIDS21, VOIDS22, VOIDS23, VOIDS24, VOIDS25, VOIDS26, VOIDS27, VOIDS28, VOIDS29, VOIDS30,
					VOIDS31, VOIDS32, VOIDS33, VOIDS34, VOIDS35");
			if (CostMode>0)
				sbSQLData.Append(@",
					sum(C1) as C1, sum(C2) as C2, sum(C3) as C3, sum(C4) as C4, sum(C5) as C5, sum(C6) as C6, sum(C7) as C7,  sum(C8) as C8, sum(C9) as C9, sum(C10) as C10,
						sum(C11) as C11, sum(C12) as C12, sum(C13) as C13, sum(C14) as C14, sum(C15) as C15, sum(C16) as C16, sum(C17) as C17, sum(C18) as C18, sum(C19) as C19, sum(C20) as C20,
						sum(C21) as C21, sum(C22) as C22, sum(C23) as C23, sum(C24) as C24, sum(C25) as C25, sum(C26) as C26, sum(C27) as C27, sum(C28) as C28, sum(C29) as C29, sum(C30) as C30,
						sum(C23) as C31, sum(C32) as C32, sum(C33) as C33, sum(C34) as C34, sum(C35) as C35");
			sbSQLData.Append(@",
					sum(TotalSales) as TotalSales,
					sum(Discounts) as Discounts,
					sum(NetSales) as NetSales,
					sum(Quantity) as Quantity,
					sum(TotalVoids) as TotalVoids,
					sum(TotalRefunds) as TotalRefunds,
					sum(TotalSelectedCharges1) as TotalSelectedCharges1,
					sum(TotalSelectedCharges2) as TotalSelectedCharges2,
					sum(TotalSelectedCharges0) as TotalSelectedCharges0");
			if (CostMode==0)
				sbSQLData.Append(@",
					sum(RoyaltyFoodSales) as RoyaltyFoodSales,
					sum(RoyaltyBeverageSales) as RoyaltyBeverageSales,
					sum(RoyaltyOtherSales) as RoyaltyOtherSales,
					sum(RoyaltyDiscounts) as RoyaltyDiscounts,
					sum(RoyaltyExcludedDiscounts) as RoyaltyExcludedDiscounts");
			sbSQLData.Append(@",
					null as RoyaltyColumn1Amount,
					null as RoyaltyColumn1Label,
					0 as RoyaltyColumn1DisplayAttribute,
					null as RoyaltyColumn2Amount,
					null as RoyaltyColumn2Label,
					0 as RoyaltyColumn2DisplayAttribute,
					null as RoyaltyColumn3Amount,
					null as RoyaltyColumn3Label,
					0 as RoyaltyColumn3DisplayAttribute,
					null as RoyaltyColumn4Amount,
					null as RoyaltyColumn4Label,
					0 as RoyaltyColumn4DisplayAttribute");
			if (CostMode==1||CostMode==2) {
				sbSQLData.Append (@",
					0 as TotalQuantity,
					case when sum(TotalSales)<>0 then floor(10000*sum(ProductCost)/sum(TotalSales)+0.5)/100 else 0 end as CostGrossPercentage,
					case when sum(TotalSales)<>0 then floor(10000*sum(Discounts)/sum(TotalSales)+0.5)/100 else 0 end as DiscountsPercentage,
					case when sum(NetSales)<>0 then floor(10000*sum(ProductCost)/sum(NetSales)+0.5)/100 else 0 end as CostNetPercentage,
					case when sum(TotalSales)<>0 then floor(100*sum(ProductCost)*(1+sum(Discounts)/sum(TotalSales))+0.5)/100 else 0 end as AdjustedProductCost,
					case when sum(TotalSales)+sum(Discounts)<>0 and sum(TotalSales)<>0 then floor(10000*(sum(ProductCost)*(1+sum(Discounts)/sum(TotalSales))/(sum(TotalSales)+sum(Discounts)))+0.5)/100 else 0 end as AdjustedProductCostPercentage,
					case when sum(TotalSales)<>0 then floor(100*((sum(TotalSales)+sum(Discounts))-sum(ProductCost)*(1+sum(Discounts)/sum(TotalSales)))+0.5)/100 else 0 end as AdjustedNetProfit,
					case when sum(TotalSales)+sum(Discounts)<>0 and sum(TotalSales)<>0 then 100-floor(10000*(sum(ProductCost)*(1+sum(Discounts)/sum(TotalSales))/(sum(TotalSales)+sum(Discounts)))+0.5)/100 else 0 end as AdjustedNetProfitPercentage");
				if (MenuItemFlag==1)
					sbSQLData.Append (@",
					MenuItemPrice,
					sum(ProductCost) as ProductCost,
					UnitCost");
				else
					sbSQLData.Append (@",
					0.00 as MenuItemPrice,
					sum(ProductCost) as ProductCost,
					0.00 as UnitCost");
				if (CostMode==1||CostMode==2) {
					sbSQLData.Append(@",
					UnitCostSource,
					0 as PercentOfTotalItems,
					0 as PercentOfMenuMix,
					0 as GrossProfitPerPlate,
					0 as ItemSales,
					0 as ItemCost,
					0 as ItemGrossProfit,
					0 as PlateGrossProfit,
					0 as PlateAverageProfit,
					0 as MenuItemProfitabilityRatingID,
					null as MenuItemProfitabilityRating,
					0 as MenuItemProfitabilityRatingSort,
					dbo.fn_CalcDayNumberDate(@StartDayNumber) as ReportStartDate,
					dbo.fn_CalcDayNumberDate(@EndDayNumber) as ReportEndDate");
					}
				}
			sbSQLData.Append(@"
				from @temp_table tt
				group by
					CompareIndex,
					StoreGroupID,
					StoreGroup,
					StoreGroupSort," + "\n");
			if (StoreFlag==1)
				sbSQLData.Append(@"					StoreID,
					StoreNumber,
					StoreName,
					StoreGCode," + "\n");
			sbSQLData.Append(@"					ProfitCenter,
					DayPartID,
					DayPart,
					DayPartSort,
					DayHourNumber,
					DayHourName,
					FrequentDinerPlanID,
					FrequentDinerPlan,
					FrequentDinerCustomerID,
					FrequentDinerCustomer," + "\n");
			if (StoreFlag==1)
				sbSQLData.Append(@"					StoreStaffID," + "\n");
			sbSQLData.Append(@"					StoreStaff,
					CheckNumberID,
					CheckNumber,
					MainTypeID, 
					MainType,
					MainGroup,
					SaleType");
			if (MenuItemPLUFlag==1)
				sbSQLData.Append (@",
					MenuItemID,
					MenuItemPLU");
			sbSQLData.Append (@",
					case when MenuItemID=0 or MenuItemID>10000000 or left(MenuItem,9)='&lt;PLU: ' and right(MenuItem,4)='&gt;' then 0 else 1 end,
					MenuItem");
			if (CostMode>0 && MenuItemFlag==1)
				sbSQLData.Append (@",
					MenuItemPrice,
					UnitCost");
			if (CostMode>0)
				sbSQLData.Append (@",
					UnitCostSource");
			sbSQLData.Append (@",
					case when DiscountID=0 then 0 else 1 end,
					Discount,
					Special,
					WeekNumber,
					WeekName,
					WeekDayNumber,
					WeekDayName,
					DayNumber,
					DayName");
			//if (IncludeVoids)
				sbSQLData.Append(@",
					VOIDS1, VOIDS2, VOIDS3, VOIDS4, VOIDS5, VOIDS6, VOIDS7, VOIDS8, VOIDS9, VOIDS10,
						VOIDS11, VOIDS12, VOIDS13, VOIDS14, VOIDS15, VOIDS16, VOIDS17, VOIDS18, VOIDS19, VOIDS20,
						VOIDS21, VOIDS22, VOIDS23, VOIDS24, VOIDS25, VOIDS26, VOIDS27, VOIDS28, VOIDS29, VOIDS30,
						VOIDS31, VOIDS32, VOIDS33, VOIDS34, VOIDS35");
			if (DiscountFlag==1)
				sbSQLData.Append(@"
				declare @DiscountAmount money
				set @DiscountAmount = 0
				declare @RowID int
				declare @Discounts money
				declare @DiscountRow bit
				declare cur cursor for select RowID, Discounts, DiscountRow from @header_table ht order by RowID desc
				open cur
				fetch next from cur into @RowID, @Discounts, @DiscountRow
				while @@fetch_status=0
					begin
					if @DiscountRow=0
						begin
						update @header_table
							set Discounts=@DiscountAmount, NetSales=TotalSales+@DiscountAmount
							where RowID=@RowID
						set @DiscountAmount = 0
						end
					else
						set @DiscountAmount = @DiscountAmount + isnull(@Discounts,0)
					fetch next from cur into @RowID, @Discounts, @DiscountRow
					end
				close cur
				deallocate cur" + "\n");
			if ((CostMode == 1 || CostMode == 2) && (MenuItemFlag==1 || MenuItemPLUFlag==1)) {
				sbSQLData.Append(@"
				declare @TotalQuantity money
				declare @TotalSales money
				declare @TotalCost money
				declare @TotalProfit money
				declare @PercentOfMenuMix decimal(18,12)
				declare @CountOfItems decimal(18,12)
				set @CountOfItems = (select count(*) as CountOfItems from @header_table ht)
				declare cur cursor for
					select
						sum(Quantity) as TotalQuantity,
						sum(TotalSales) as TotalSales,
						sum(ProductCost) as TotalCost
					from
						@header_table ht
				open cur
				fetch next from cur into @TotalQuantity, @TotalSales, @TotalCost
				if @@fetch_status=0
					begin
					if @CountOfItems<>0
						set @PercentOfMenuMix = (1 / @CountOfItems) * @MenuMixPercentOfTotalFactor
					else
						set @PercentOfMenuMix = 0
					set @TotalProfit = @TotalSales - @TotalCost
					update @header_table
						set
							TotalQuantity=@TotalQuantity,
							PercentOfTotalItems = case when cast(@TotalQuantity as decimal(18,12))<>0 then 100 * cast(Quantity as decimal(18,12)) / cast(@TotalQuantity as decimal(18,12)) else 0 end,
							PercentOfMenuMix = 100 * @PercentOfMenuMix,
							GrossProfitPerPlate = (MenuItemPrice - UnitCost),
							ItemSales = TotalSales,
							ItemCost = ProductCost,
							ItemGrossProfit = TotalSales - ProductCost,
							PlateGrossProfit = case when cast(Quantity as decimal(18,12))<>0 then (TotalSales - ProductCost) / cast(Quantity as decimal(18,12)) else 0 end,
							PlateAverageProfit = case when @TotalQuantity<>0 then (@TotalProfit / @TotalQuantity) else 0 end,
							MenuItemProfitabilityRatingID =
								case
									when @TotalQuantity=0
										then 0
									when cast(@TotalQuantity as decimal(18,12))=0
										then 0
									when cast(Quantity as decimal(18,12))=0
										then 0
									when (TotalSales - ProductCost) / cast(Quantity as decimal(18,12))<(@TotalProfit / @TotalQuantity)
										then
											case
												when cast(Quantity as decimal(18,12)) / cast(@TotalQuantity as decimal(18,12)) < @PercentOfMenuMix
												then 0
												else 1
											end
									else
										case
											when cast(@TotalQuantity as decimal(18,12))=0 then 0
											when cast(Quantity as decimal(18,12)) / cast(@TotalQuantity as decimal(18,12)) < @PercentOfMenuMix
											then 2
											else 3
										end
								end
					end
				close cur
				deallocate cur
				declare @MenuItemProfitabilityRatingID int
				declare @MenuItemProfitabilityRating varchar(50)
				declare cur cursor for select MenuItemProfitabilityRatingID, MenuItemProfitabilityRating from tblMenuItemProfitabilityRatings
				open cur
				fetch next from cur into @MenuItemProfitabilityRatingID, @MenuItemProfitabilityRating
				while @@fetch_status=0
					begin
					update @header_table
						set
							MenuItemProfitabilityRating=@MenuItemProfitabilityRating,
							MenuItemProfitabilityRatingSort = cast(10000000000 * MenuItemProfitabilityRatingID as money) + PlateGrossProfit
						where MenuItemProfitabilityRatingID=@MenuItemProfitabilityRatingID
					fetch next from cur into @MenuItemProfitabilityRatingID, @MenuItemProfitabilityRating
					end
				close cur
				deallocate cur" + "\n");
				}
			sbSQLData.Append(@"
				update @header_table
					set FrequentDinerCustomer = '{Customer #'+cast(FrequentDinerCustomerID as varchar(12))+'}'
					where isnull(FrequentDinerCustomerID,0)<>0 and len(FrequentDinerCustomer)=0");
			if (CostMode==0)
				sbSQLData.Append(@"
				if (@StoreFlag<>0)
					begin
					update ht
						set ht.RoyaltyColumn1Label=isnull(roy.RoyaltyName1,''),
							ht.RoyaltyColumn1Amount=isnull(roy.RoyaltyPercentage1/100.0 * (
								case when roy.Food1<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage1<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other1<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts1<>0 then RoyaltyDiscounts else 0 end
								),0),
							ht.RoyaltyColumn2Label=isnull(roy.RoyaltyName2,''),
							ht.RoyaltyColumn2Amount=isnull(roy.RoyaltyPercentage2/100.0 * (
								case when roy.Food2<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage2<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other2<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts2<>0 then RoyaltyDiscounts else 0 end							
								),0),
							ht.RoyaltyColumn3Label=isnull(roy.RoyaltyName3,''),
							ht.RoyaltyColumn3Amount=isnull(roy.RoyaltyPercentage3/100.0 * (
								case when roy.Food3<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage3<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other3<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts3<>0 then RoyaltyDiscounts else 0 end
								),0),
							ht.RoyaltyColumn4Label=isnull(roy.RoyaltyName4,''),
							ht.RoyaltyColumn4Amount=isnull(roy.RoyaltyPercentage4/100.0 * (
								case when roy.Food4<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage4<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other4<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts4<>0 then RoyaltyDiscounts else 0 end							
								),0)
					from
						@header_table ht
					inner join (
						select isnull(dcn1.StoreID, dcn2.StoreID) as StoreID,
							dcn1.RoyaltyName1, dcn1.RoyaltyPercentage1, dcn1.Food1, dcn1.Beverage1, dcn1.Other1, dcn1.Discounts1,
							dcn2.RoyaltyName2, dcn2.RoyaltyPercentage2, dcn2.Food2, dcn2.Beverage2, dcn2.Other2, dcn2.Discounts2,
							dcn3.RoyaltyName3, dcn3.RoyaltyPercentage3, dcn3.Food3, dcn3.Beverage3, dcn3.Other3, dcn3.Discounts3,
							dcn4.RoyaltyName4, dcn4.RoyaltyPercentage4, dcn4.Food4, dcn4.Beverage4, dcn4.Other4, dcn4.Discounts4
						from (
							select
								ry.StoreID, 1 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName1, ry.RoyaltyPercentage as RoyaltyPercentage1, 
									ry.Food as Food1, ry.Beverage as Beverage1, ry.Other as Other1, ry.Discounts as Discounts1
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							where DisplayColumnNumber=1
							) dcn1
						full outer join (
							select
								ry.StoreID, 2 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName2, ry.RoyaltyPercentage as RoyaltyPercentage2, 
									ry.Food as Food2, ry.Beverage as Beverage2, ry.Other as Other2, ry.Discounts as Discounts2
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							where DisplayColumnNumber=2
							) dcn2
						on dcn2.StoreID=dcn1.StoreID
						full outer join (
							select
								ry.StoreID, 3 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName3, ry.RoyaltyPercentage as RoyaltyPercentage3, 
									ry.Food as Food3, ry.Beverage as Beverage3, ry.Other as Other3, ry.Discounts as Discounts3
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							where DisplayColumnNumber=3
							) dcn3
						on dcn3.StoreID=isnull(dcn1.StoreID,dcn2.StoreID)
						full outer join (
							select
								ry.StoreID, 4 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName4, ry.RoyaltyPercentage as RoyaltyPercentage4, 
									ry.Food as Food4, ry.Beverage as Beverage4, ry.Other as Other4, ry.Discounts as Discounts4
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							where DisplayColumnNumber=4
							) dcn4
						on dcn4.StoreID=isnull(dcn1.StoreID,dcn2.StoreID)
						) roy
					on roy.StoreID=ht.StoreID
					update ht
						set RoyaltyColumn1label = uq.RoyaltyColumn1Label,
						RoyaltyColumn2label = uq.RoyaltyColumn2Label,
						RoyaltyColumn3label = uq.RoyaltyColumn3Label,
						RoyaltyColumn4label = uq.RoyaltyColumn4Label
					from
						@header_table ht
					cross join (
						select
							max(RoyaltyColumn1Label) as RoyaltyColumn1Label,
							max(RoyaltyColumn2Label) as RoyaltyColumn2Label,
							max(RoyaltyColumn3Label) as RoyaltyColumn3Label,
							max(RoyaltyColumn4Label) as RoyaltyColumn4Label
						from @header_table ht
						) uq
					end
				if (@StoreFlag=0 and @StoreGroupFlag<>0)
					begin
					update ht
						set ht.RoyaltyColumn1Label=roy.RoyaltyName1,
							ht.RoyaltyColumn1Amount=roy.RoyaltyPercentage1/100.0 * (
								case when roy.Food1<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage1<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other1<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts1<>0 then RoyaltyDiscounts else 0 end
								),
							ht.RoyaltyColumn2Label=roy.RoyaltyName2,
							ht.RoyaltyColumn2Amount=roy.RoyaltyPercentage2/100.0 * (
								case when roy.Food2<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage2<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other2<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts2<>0 then RoyaltyDiscounts else 0 end							
								),
							ht.RoyaltyColumn3Label=roy.RoyaltyName3,
							ht.RoyaltyColumn3Amount=roy.RoyaltyPercentage3/100.0 * (
								case when roy.Food3<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage3<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other3<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts3<>0 then RoyaltyDiscounts else 0 end
								),
							ht.RoyaltyColumn4Label=roy.RoyaltyName4,
							ht.RoyaltyColumn4Amount=roy.RoyaltyPercentage4/100.0 * (
								case when roy.Food4<>0 then RoyaltyFoodSales else 0 end +
								case when roy.Beverage4<>0 then RoyaltyBeverageSales else 0 end +
								case when roy.Other4<>0 then RoyaltyOtherSales else 0 end +
								case when roy.Discounts4<>0 then RoyaltyDiscounts else 0 end							
								)
					from
						@header_table ht
					inner join (
						select isnull(dcn1.StoreGroupID, dcn2.StoreGroupID) as StoreGroupID,
							dcn1.RoyaltyName1, dcn1.RoyaltyPercentage1, dcn1.Food1, dcn1.Beverage1, dcn1.Other1, dcn1.Discounts1,
							dcn2.RoyaltyName2, dcn2.RoyaltyPercentage2, dcn2.Food2, dcn2.Beverage2, dcn2.Other2, dcn2.Discounts2,
							dcn3.RoyaltyName3, dcn3.RoyaltyPercentage3, dcn3.Food3, dcn3.Beverage3, dcn3.Other3, dcn3.Discounts3,
							dcn4.RoyaltyName4, dcn4.RoyaltyPercentage4, dcn4.Food4, dcn4.Beverage4, dcn4.Other4, dcn4.Discounts4
						from (
							select distinct
								sto.StoreGroupID, 1 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName1, ry.RoyaltyPercentage as RoyaltyPercentage1, 
									ry.Food as Food1, ry.Beverage as Beverage1, ry.Other as Other1, ry.Discounts as Discounts1
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							inner join
								tblStores sto
							on sto.StoreID=ry.StoreID
							where DisplayColumnNumber=1
							) dcn1
						full outer join (
							select distinct
								sto.StoreGroupID, 1 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName2, ry.RoyaltyPercentage as RoyaltyPercentage2, 
									ry.Food as Food2, ry.Beverage as Beverage2, ry.Other as Other2, ry.Discounts as Discounts2
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							inner join
								tblStores sto
							on sto.StoreID=ry.StoreID
							where DisplayColumnNumber=2
							) dcn2
						on dcn2.StoreGroupID=dcn1.StoreGroupID
						full outer join (
							select distinct
								sto.StoreGroupID, 3 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName3, ry.RoyaltyPercentage as RoyaltyPercentage3, 
									ry.Food as Food3, ry.Beverage as Beverage3, ry.Other as Other3, ry.Discounts as Discounts3
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							inner join
								tblStores sto
							on sto.StoreID=ry.StoreID
							where DisplayColumnNumber=3
							) dcn3
						on dcn3.StoreGroupID=isnull(dcn1.StoreGroupID,dcn2.StoreGroupID)
						full outer join (
							select distinct
								sto.StoreGroupID, 4 as DisplayColumnNumber, rt.RoyaltyName as RoyaltyName4, ry.RoyaltyPercentage as RoyaltyPercentage4, 
									ry.Food as Food4, ry.Beverage as Beverage4, ry.Other as Other4, ry.Discounts as Discounts4
							from
								tblRoyalties ry
							inner join
								tblRoyaltyTypes rt
							on ry.RoyaltyTypeID=rt.RoyaltyTypeID
							inner join
								tblStores sto
							on sto.StoreID=ry.StoreID
							where DisplayColumnNumber=4
							) dcn4
						on dcn4.StoreGroupID=isnull(dcn1.StoreGroupID,dcn2.StoreGroupID)
						) roy
					on roy.StoreGroupID=ht.StoreGroupID
					update ht
						set RoyaltyColumn1label = uq.RoyaltyColumn1Label,
						RoyaltyColumn2label = uq.RoyaltyColumn2Label,
						RoyaltyColumn3label = uq.RoyaltyColumn3Label,
						RoyaltyColumn4label = uq.RoyaltyColumn4Label
					from
						@header_table ht
					cross join (
						select
							max(RoyaltyColumn1Label) as RoyaltyColumn1Label,
							max(RoyaltyColumn2Label) as RoyaltyColumn2Label,
							max(RoyaltyColumn3Label) as RoyaltyColumn3Label,
							max(RoyaltyColumn4Label) as RoyaltyColumn4Label
						from @header_table ht
						) uq
					end
				if @StoreFlag<>0 or @StoreGroupFlag<>0
					begin
					update
						ht
					set
						RoyaltyColumn1DisplayAttribute = case when isnull(nsq.DisplayColumnNumber,0)=1 then 1 else RoyaltyColumn1DisplayAttribute end
					from
						@header_table ht
					inner join (
						select
							rb.StoreID, rq.DisplayColumnNumber
						from (
							select rb.RoyaltyTypeID, rb.RoyaltyPercentage, rtb.DisplayColumnNumber from tblRoyaltiesBase rb inner join tblRoyaltyTypesBase rtb on rtb.RoyaltyTypeID=rb.RoyaltyTypeID where StoreID=0
							) rq
						inner join
							tblRoyaltiesBase rb
						on rq.RoyaltyTypeID=rb.RoyaltyTypeID
						where rb.StoreID<>0 and rq.RoyaltyPercentage<>rb.RoyaltyPercentage and rq.RoyaltyTypeID=1
						) nsq
					on nsq.StoreID=ht.StoreID
					update
						ht
					set
						RoyaltyColumn2DisplayAttribute = case when isnull(nsq.DisplayColumnNumber,0)=2 then 1 else RoyaltyColumn2DisplayAttribute end
					from
						@header_table ht
					inner join (
						select
							rb.StoreID, rq.DisplayColumnNumber
						from (
							select rb.RoyaltyTypeID, rb.RoyaltyPercentage, rtb.DisplayColumnNumber from tblRoyaltiesBase rb inner join tblRoyaltyTypesBase rtb on rtb.RoyaltyTypeID=rb.RoyaltyTypeID where StoreID=0
							) rq
						inner join
							tblRoyaltiesBase rb
						on rq.RoyaltyTypeID=rb.RoyaltyTypeID
						where rb.StoreID<>0 and rq.RoyaltyPercentage<>rb.RoyaltyPercentage and rq.RoyaltyTypeID=2
						) nsq
					on nsq.StoreID=ht.StoreID
					update
						ht
					set
						RoyaltyColumn3DisplayAttribute = case when isnull(nsq.DisplayColumnNumber,0)=3 then 1 else RoyaltyColumn3DisplayAttribute end
					from
						@header_table ht
					inner join (
						select
							rb.StoreID, rq.DisplayColumnNumber
						from (
							select rb.RoyaltyTypeID, rb.RoyaltyPercentage, rtb.DisplayColumnNumber from tblRoyaltiesBase rb inner join tblRoyaltyTypesBase rtb on rtb.RoyaltyTypeID=rb.RoyaltyTypeID where StoreID=0
							) rq
						inner join
							tblRoyaltiesBase rb
						on rq.RoyaltyTypeID=rb.RoyaltyTypeID
						where rb.StoreID<>0 and rq.RoyaltyPercentage<>rb.RoyaltyPercentage and rq.RoyaltyTypeID=3
						) nsq
					on nsq.StoreID=ht.StoreID
					update
						ht
					set
						RoyaltyColumn4DisplayAttribute = case when isnull(nsq.DisplayColumnNumber,0)=4 then 1 else RoyaltyColumn4DisplayAttribute end
					from
						@header_table ht
					inner join (
						select
							rb.StoreID, rq.DisplayColumnNumber
						from (
							select rb.RoyaltyTypeID, rb.RoyaltyPercentage, rtb.DisplayColumnNumber from tblRoyaltiesBase rb inner join tblRoyaltyTypesBase rtb on rtb.RoyaltyTypeID=rb.RoyaltyTypeID where StoreID=0
							) rq
						inner join
							tblRoyaltiesBase rb
						on rq.RoyaltyTypeID=rb.RoyaltyTypeID
						where rb.StoreID<>0 and rq.RoyaltyPercentage<>rb.RoyaltyPercentage and rq.RoyaltyTypeID=4
						) nsq
					on nsq.StoreID=ht.StoreID
					end"+"\n");
			if (CostMode==1 || CostMode==2)
				sbSQLData.Append(@"
					select /** select **/ ht.*,
						case UnitCostSource
							when 1 then 'Sale<br />Type'
							when 2 then 'Inventory'
							when 3 then 'Sale Type<br />Inventory'
							when 4 then 'Plate<br />Cost'
							when 5 then 'Sale Type<br />Plate Cost'
							when 6 then 'Inventory<br />Plate Cost'
							when 7 then 'Sale Type<br />Inventory<br />Plate Cost'
							else null
						end as UnitCostSourceDisplay /** from **/ from @header_table ht");
			else
				if (StoreFlag!=0 && StoreListCSV.Length==0 || RawExport)
					sbSQLData.Append(@"
						select /** select **/ " + (RawExport?"RowID, ":"isnull(RowID,-sto.StoreID) as RowID, CompareIndex, ")
							+ (!RawExport || DayPartFlag!=0?"DayPartID, DayPart, DayPartSort, ":"")
							+ (!RawExport || DayHourFlag!=0?"DayHourNumber, DayHourName, ":"")
							+ (!RawExport || FrequentDinerPlanFlag!=0?"FrequentDinerPlanID, FrequentDinerPlan, ":"")
							+ (!RawExport || FrequentDinerCustomerFlag!=0?"FrequentDinerCustomerID, FrequentDinerCustomer, ":"")
							+ (!RawExport || StoreStaffFlag!=0?"StoreStaffID, StoreStaff, ":"")
							+ (!RawExport || CheckNumberFlag!=0?"CheckNumberID, CheckNumber, ":"")
							+ (!RawExport || StoreGroupFlag!=0?"ht.StoreGroupID, ht.StoreGroup, ht.StoreGroupSort, ":"")
							+ (!RawExport || StoreFlag!=0?"sto.StoreID, sto.StoreNumber, isnull(sto.StoreReportName,sto.StoreName) as StoreName, sto.StoreGCode, ":"")
							+ (!RawExport || ProfitCenterFlag!=0?"ProfitCenterID, ProfitCenter, ":"")
							+ (!RawExport || MainTypeFlag!=0?"MainTypeID, MainType, ":"") 
							+ (!RawExport || MainGroupFlag!=0?"MainGroupID, MainGroup, ":"")
							+ (!RawExport || SaleTypeFlag!=0?"SaleTypeID, SaleType, ":"")
							+ (!RawExport || MenuItemFlag!=0?"MenuItemID, MenuItemRow, MenuItem, ":"")
							+ (!RawExport || MenuItemPLUFlag!=0?"MenuItemPLU, ":"")
							+ (!RawExport || DiscountFlag!=0?"DiscountID, DiscountRow, Discount, ":"")
							+ (!RawExport || SpecialFlag!=0?"Special, ":"")
							+ (!RawExport || WeekFlag!=0?"WeekNumber, WeekName, ":"")
							+ (!RawExport || WeekDayFlag!=0?"WeekDayNumber, WeekDayName, ":"")
							+ (!RawExport || DayFlag!=0?"DayNumber, DayName, ":"")
							+ (!RawExport?"BatchSubmissionStatus, BSS1, BSS2, BSS3, BSS4, BSS5, BSS6, BSS7, BSS8, BSS9, BSS10, BSS11, BSS12, BSS13, BSS14, BSS15, BSS16, BSS17, BSS18, BSS19, BSS20, BSS21, BSS22, BSS23, BSS24, BSS25, BSS26, BSS27, BSS28, BSS29, BSS30, BSS31, BSS32, BSS33, BSS34, BSS35, ":"")
							+ (!RawExport?"D1, D2, D3, D4, D5, D6, D7, D8, D9, D10, D11, D12, D13, D14, D15, D16, D17, D18, D19, D20, D21, D22, D23, D24, D25, D26, D27, D28, D29, D30, D31, D32, D33, D34, D35, ":"D1 as SalesMon, D2 as SalesTue, D3 as SalesWed, D4 as SalesThu, D5 as SalesFri, D6 as SalesSat, D7 as SalesSun, ")
							+ (!RawExport?"DS1, DS2, DS3, DS4, DS5, DS6, DS7, DS8, DS9, DS10, DS11, DS12, DS13, DS14, DS15, DS16, DS17, DS18, DS19, DS20, DS21, DS22, DS23, DS24, DS25, DS26, DS27, DS28, DS29, DS30, DS31, DS32, DS33, DS34, DS35, ":"DS1 as DiscountsMon, DS2 as DiscountsTue, DS3 as DiscountsWed, DS4 as DiscountsThu, DS5 as DiscountsFri, DS6 as DiscountsSat, DS7 as DiscountsSun, ")
							+ (!RawExport?"N1, N2, N3, N4, N5, N6, N7, N8, N9, N10, N11, N12, N13, N14, N15, N16, N17, N18, N19, N20, N21, N22, N23, N24, N25, N26, N27, N28, N29, N30, N31, N32, N33, N34, N35, ":"D1+DS1 as NetMon, D2+DS2 as NetTue, D3+DS3 as NetWed, D4+DS4 as NetThu, D5+DS5 as NetFri, D6+DS6 as NetSat, D7+DS7 as NetSun, ")
							+ (!RawExport?"Q1, Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q11, Q12, Q13, Q14, Q15, Q16, Q17, Q18, Q19, Q20, Q21, Q22, Q23, Q24, Q25, Q26, Q27, Q28, Q29, Q30, Q31, Q32, Q33, Q34, Q35, ":"Q1 as QuantityMon, Q2 as QuantityTue, Q3 as QuantityWed, Q4 as QuantityThu, Q5 as QuantityFri, Q6 as QuantitySat, Q7 as QuantitySun, ")
							+ (!RawExport?"VOIDS1, VOIDS2, VOIDS3, VOIDS4, VOIDS5, VOIDS6, VOIDS7, VOIDS8, VOIDS9, VOIDS10, VOIDS11, VOIDS12, VOIDS13, VOIDS14, VOIDS15, VOIDS16, VOIDS17, VOIDS18, VOIDS19, VOIDS20, VOIDS21, VOIDS22, VOIDS23, VOIDS24, VOIDS25, VOIDS26, VOIDS27, VOIDS28, VOIDS29, VOIDS30, VOIDS31, VOIDS32, VOIDS33, VOIDS34, VOIDS35, ":"VOIDS1 as VoidsMon, VOIDS2 as VoidsTue, VOIDS3 as VoidsWed, VOIDS4 as VoidsThu, VOIDS5 as VoidsFri, VOIDS6 as VoidsSat, VOIDS7 as VoidsSun, ") + @"
							TotalSales, Discounts, NetSales, Quantity, TotalVoids, TotalRefunds, TotalSelectedCharges1,  TotalSelectedCharges2,  TotalSelectedCharges0, 
							RoyaltyFoodSales, RoyaltyBeverageSales, RoyaltyOtherSales, RoyaltyDiscounts, RoyaltyExcludedDiscounts,
							RoyaltyColumn1Amount, (select min(RoyaltyColumn1Label) from @header_table) as RoyaltyColumn1Label, RoyaltyColumn1DisplayAttribute,
							RoyaltyColumn2Amount, (select min(RoyaltyColumn2Label) from @header_table) as RoyaltyColumn2Label, RoyaltyColumn2DisplayAttribute,
							RoyaltyColumn3Amount, (select min(RoyaltyColumn3Label) from @header_table) as RoyaltyColumn3Label, RoyaltyColumn3DisplayAttribute,
							RoyaltyColumn4Amount, (select min(RoyaltyColumn4Label) from @header_table) as RoyaltyColumn4Label, RoyaltyColumn4DisplayAttribute,
							'Total Discounts ' + cast(-RoyaltyDiscounts-RoyaltyExcludedDiscounts as varchar(20)) + char(13) + char(10)
								+ '- Discount Not Reducing Royalty ' + cast(-RoyaltyExcludedDiscounts as varchar(20)) + char(13) + char(10)
								+ '= Royalty Applicable Discounts ' + cast(-RoyaltyDiscounts as varchar(20)) + char(13) + char(10) + char(13) + char(10)
								+ 'Total Sales ' + cast(TotalSales as varchar(20)) + char(13) + char(10)
								+ ' - Royalty Applicable Discounts ' + cast(-RoyaltyDiscounts as varchar(20)) + char(13) + char(10)
								+ ' = Royalty Net Sales ' + cast(TotalSales+RoyaltyDiscounts as varchar(20)) + char(13) + char(10)
								+ case when RoyaltyColumn1Amount<>0 then char(13) + char(10) + RoyaltyColumn1Label + ' ' + cast(RoyaltyColumn1Amount as varchar(20)) else '' end
								+ case when RoyaltyColumn2Amount<>0 then char(13) + char(10) + RoyaltyColumn2Label + ' ' + cast(RoyaltyColumn2Amount as varchar(20)) else '' end
								+ case when RoyaltyColumn3Amount<>0 then char(13) + char(10) + RoyaltyColumn3Label + ' ' + cast(RoyaltyColumn3Amount as varchar(20)) else '' end
								+ case when RoyaltyColumn4Amount<>0 then char(13) + char(10) + RoyaltyColumn4Label + ' ' + cast(RoyaltyColumn4Amount as varchar(20)) else '' end + '.' as RoyaltyToolTip /** from **/
						from @header_table ht
						right outer join tblStores sto on sto.StoreID = ht.StoreID /** where **/
						where (not ht.RowID is null or sto.Active<>0)");
				else
					sbSQLData.Append(@"
						select /** select **/ ht.*,
							RoyaltyColumn1Amount, (select min(RoyaltyColumn1Label) from @header_table) as RoyaltyColumn1Label, RoyaltyColumn1DisplayAttribute,
							RoyaltyColumn2Amount, (select min(RoyaltyColumn2Label) from @header_table) as RoyaltyColumn2Label, RoyaltyColumn2DisplayAttribute,
							RoyaltyColumn3Amount, (select min(RoyaltyColumn3Label) from @header_table) as RoyaltyColumn3Label, RoyaltyColumn3DisplayAttribute,
							RoyaltyColumn4Amount, (select min(RoyaltyColumn4Label) from @header_table) as RoyaltyColumn4Label, RoyaltyColumn4DisplayAttribute,
							'Total Discounts ' + cast(-RoyaltyDiscounts-RoyaltyExcludedDiscounts as varchar(20)) + char(13) + char(10)
								+ '- Discount Not Reducing Royalty ' + cast(-RoyaltyExcludedDiscounts as varchar(20)) + char(13) + char(10)
								+ '= Royalty Applicable Discounts ' + cast(-RoyaltyDiscounts as varchar(20)) + char(13) + char(10) + char(13) + char(10)
								+ 'Total Sales ' + cast(TotalSales as varchar(20)) + char(13) + char(10)
								+ ' - Royalty Applicable Discounts ' + cast(-RoyaltyDiscounts as varchar(20)) + char(13) + char(10)
								+ ' = Royalty Net Sales ' + cast(TotalSales+RoyaltyDiscounts as varchar(20)) + char(13) + char(10)
								+ case when RoyaltyColumn1Amount<>0 then char(13) + char(10) + RoyaltyColumn1Label + ' ' + cast(RoyaltyColumn1Amount as varchar(20)) else '' end
								+ case when RoyaltyColumn2Amount<>0 then char(13) + char(10) + RoyaltyColumn2Label + ' ' + cast(RoyaltyColumn2Amount as varchar(20)) else '' end
								+ case when RoyaltyColumn3Amount<>0 then char(13) + char(10) + RoyaltyColumn3Label + ' ' + cast(RoyaltyColumn3Amount as varchar(20)) else '' end
								+ case when RoyaltyColumn4Amount<>0 then char(13) + char(10) + RoyaltyColumn4Label + ' ' + cast(RoyaltyColumn4Amount as varchar(20)) else '' end + '.' as RoyaltyToolTip /** from **/ from @header_table ht");
			string strBaseSort = "";
			string strOrderBy = "";
			if (OffsetDays!=System.Int32.MinValue) {
				strBaseSort = strOrderBy;
				strOrderBy += ", CompareIndex";
				}
			if (StoreGroupFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", StoreGroup";
				}
			if (StoreFlag==1) {
				if (CostMode==0 && StoreFlag!=0 && StoreListCSV.Length==0 || RawExport) {
					if (StoreListCSV.Length==0) {
						strBaseSort = strOrderBy;
						strOrderBy += ", sto.StoreNumber";
						}
					else {
						strBaseSort = strOrderBy;
						strOrderBy += ", sto.StoreNumber";
						}
					}
				else {
					if (StoreListCSV.Length==0) {
						strBaseSort = strOrderBy;
						strOrderBy += ", StoreNumber";
						}
					else {
						strBaseSort = strOrderBy;
						strOrderBy += ", StoreNumber";
						}
					}
				}
			if (ProfitCenterFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", ProfitCenter";
				}
			if (FrequentDinerPlanFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", FrequentDinerPlan";
				}
			if (FrequentDinerCustomerFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", FrequentDinerCustomer";
				}
			if (StoreStaffFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", StoreStaff";
				}
			if (CheckNumberFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", CheckNumberID";
				}
			if (MainTypeFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", MainTypeID";
				}
			if (MainGroupFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", MainGroup";
				}
			if (SaleTypeFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", SaleType";
				}
			if (MenuItemFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", MenuItemRow, MenuItem";
				}
			if (MenuItemPLUFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", MenuItemID";
				}
			if (DiscountFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", DiscountRow, Discount";
				}
			if (WeekFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", WeekNumber";
				}
			if (WeekDayFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", WeekDayNumber";
				}
			if (DayFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", DayNumber";
				}
			if (DayPartFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", DayPartID";
				}
			if (DayHourFlag==1) {
				strBaseSort = strOrderBy;
				strOrderBy += ", DayHourNumber";
				}
			DefaultSort = "";
			if (strOrderBy.Length>0) {
				sbSQLData.Append(" /** order by **/ order by " + strOrderBy.Substring(2));
				DefaultSort = strOrderBy.Substring(2);
				}
			if (strBaseSort.Length>0) {
				BaseSort = strBaseSort.Substring(2);
				}
			sbSQLData.Append ("\n");
			return (sbSQLData.ToString());
			}

		private String strSQLHeader(String StoreListAuthorizedSales) {
			StringBuilder strSQLHeader = new StringBuilder();
			strSQLHeader.Append(
			@"declare @StartDayNumber int
				declare @EndDayNumber int
				declare @StoreGroupFlag bit
				declare @StoreGroupListCSV varchar(8000)
				declare @StoreFlag bit
				declare @StoreListCSV varchar(8000)
				declare @ProfitCenterFlag bit
				declare @ProfitCenterListCSV varchar(8000)
				declare @DayPartFlag bit
				declare @DayPartListCSV varchar(8000)
				declare @DayHourFlag bit
				declare @DayHourListCSV varchar(8000)
				declare @FrequentDinerPlanFlag bit
				declare @FrequentDinerPlanListCSV varchar(8000)
				declare @FrequentDinerCustomerFlag bit
				declare @FrequentDinerCustomerListCSV varchar(8000)
				declare @StoreStaffFlag bit
				declare @StoreStaffListCSV varchar(8000)
				declare @CheckNumberFlag bit
				declare @CheckNumberListCSV varchar(8000)
				declare @MainTypeFlag bit
				declare @MainTypeListCSV varchar(8000)
				declare @MainGroupFlag bit
				declare @MainGroupListCSV varchar(8000)
				declare @SaleTypeFlag bit
				declare @SaleTypeListCSV varchar(8000)
				declare @MenuItemFlag bit
				declare @MenuItemListCSV varchar(8000)
				declare @MenuItemPLUFlag bit
				declare @MenuItemPLUListCSV varchar(8000)
				declare @DiscountFlag bit
				declare @DiscountListCSV varchar(8000)
				declare @SpecialFlag bit
				declare @SpecialListCSV varchar(8000)
				declare @WeekFlag bit
				declare @WeekListCSV varchar(8000)
				declare @WeekDayFlag bit
				declare @WeekDayListCSV varchar(8000)
				declare @DayFlag bit
				declare @DayListCSV varchar(8000)
				");
			String StoreListCSVTemp = "";
			if (StoreListAuthorizedSales!=StoreListCSV)
				StoreListCSVTemp = StoreListCSV;
			strSQLHeader.Append( 
				@"set @StartDayNumber=" + StartDayNumber.ToString()+"\n"
				+ @"set @EndDayNumber=" + EndDayNumber.ToString()+"\n"
				+ @"set @StoreGroupFlag=" + StoreGroupFlag.ToString()+"\n"
				+ @"set @StoreGroupListCSV='" + StoreGroupListCSV.ToString()+"'\n"
				+ @"set @StoreFlag=" + StoreFlag.ToString()+"\n"
				+ @"set @StoreListCSV='" + StoreListCSVTemp.ToString()+"'\n"
				+ @"set @ProfitCenterFlag=" + ProfitCenterFlag.ToString()+"\n"
				+ @"set @ProfitCenterListCSV='" + ProfitCenterListCSV.ToString()+"'\n"
				+ @"set @DayPartFlag=" + DayPartFlag.ToString()+"\n"
				+ @"set @DayPartListCSV='" + DayPartListCSV.ToString()+"'\n"
				+ @"set @DayHourFlag=" + DayHourFlag.ToString()+"\n"
				+ @"set @DayHourListCSV='" + DayHourListCSV.ToString()+"'\n"
				+ @"set @FrequentDinerPlanFlag=" + FrequentDinerPlanFlag.ToString()+"\n"
				+ @"set @FrequentDinerPlanListCSV='" + FrequentDinerPlanListCSV.ToString()+"'\n"
				+ @"set @FrequentDinerCustomerFlag=" + FrequentDinerCustomerFlag.ToString()+"\n"
				+ @"set @FrequentDinerCustomerListCSV='" + FrequentDinerCustomerListCSV.ToString()+"'\n"
				+ @"set @StoreStaffFlag=" + StoreStaffFlag.ToString()+"\n"
				+ @"set @StoreStaffListCSV='" + StoreStaffListCSV.ToString()+"'\n"
				+ @"set @CheckNumberFlag=" + CheckNumberFlag.ToString()+"\n"
				+ @"set @CheckNumberListCSV='" + CheckNumberListCSV.ToString()+"'\n"
				+ @"set @MainTypeFlag=" + MainTypeFlag.ToString()+"\n"
				+ @"set @MainTypeListCSV='" + MainTypeListCSV.ToString()+"'\n"
				+ @"set @MainGroupFlag=" + MainGroupFlag.ToString()+"\n"
				+ @"set @MainGroupListCSV='" + MainGroupListCSV.ToString()+"'\n"
				+ @"set @SaleTypeFlag=" + SaleTypeFlag.ToString()+"\n"
				+ @"set @SaleTypeListCSV='" + SaleTypeListCSV.ToString()+"'\n"
				+ @"set @MenuItemFlag=" + MenuItemFlag.ToString()+"\n"
				+ @"set @MenuItemListCSV='" + MenuItemListCSV.ToString()+"'\n"
				+ @"set @MenuItemPLUFlag=" + MenuItemPLUFlag.ToString()+"\n"
				+ @"set @MenuItemPLUListCSV='" + MenuItemPLUListCSV.ToString()+"'\n"
				+ @"set @DiscountFlag=" + DiscountFlag.ToString()+"\n"
				+ @"set @DiscountListCSV='" + DiscountListCSV.ToString()+"'\n"
				+ @"set @SpecialFlag=" + SpecialFlag.ToString()+"\n"
				+ @"set @SpecialListCSV='" + SpecialListCSV.ToString()+"'\n"
				+ @"set @WeekFlag=" + WeekFlag.ToString()+"\n"
				+ @"set @WeekListCSV='" + WeekListCSV.ToString()+"'\n"
				+ @"set @WeekDayFlag=" + WeekDayFlag.ToString()+"\n"
				+ @"set @WeekDayListCSV='" + WeekDayListCSV.ToString()+"'\n"
				+ @"set @DayFlag=" + DayFlag.ToString()+"\n"
				+ @"set @DayListCSV='" + DayListCSV.ToString()+"'\n");
			strSQLHeader.Append(@"
			declare @table_variable TABLE (
				StoreGroup varchar(400),
				StoreGroupDetailFlag bit,
				StoreGroupMultipleFlag bit,
				StoreName varchar(1200),
				StoreDetailFlag bit,
				StoreMultipleFlag bit,
				ProfitCenter varchar(400),
				ProfitCenterDetailFlag bit,
				ProfitCenterMultipleFlag bit,
				DayPart varchar(200),
				DayPartDetailFlag bit,
				DayPartMultipleFlag bit,
				DayHourName varchar(200),
				DayHourDetailFlag bit,
				DayHourMultipleFlag bit,
				FrequentDinerPlan varchar(200),
				FrequentDinerPlanDetailFlag bit,
				FrequentDinerPlanMultipleFlag bit,
				FrequentDinerCustomer varchar(300),
				FrequentDinerCustomerDetailFlag bit,
				FrequentDinerCustomerMultipleFlag bit,
				StoreStaff varchar(200),
				StoreStaffDetailFlag bit,
				StoreStaffMultipleFlag bit,
				CheckNumber varchar(200),
				CheckNumberDetailFlag bit,
				CheckNumberMultipleFlag bit,
				MainType varchar(200),
				MainTypeDetailFlag bit,
				MainTypeMultipleFlag bit,
				MainGroup varchar(300),
				MainGroupDetailFlag bit,
				MainGroupMultipleFlag bit,
				SaleType varchar(700),
				SaleTypeDetailFlag bit,
				SaleTypeMultipleFlag bit,
				MenuItem varchar(1400),
				MenuItemDetailFlag bit,
				MenuItemMultipleFlag bit,
				MenuItemPLU varchar(600),
				MenuItemPLUDetailFlag bit,
				MenuItemPLUMultipleFlag bit,
				Discount varchar(800),
				DiscountDetailFlag bit,
				DiscountMultipleFlag bit,
				Special varchar(100),
				SpecialDetailFlag bit,
				SpecialMultipleFlag bit,
				WeekName varchar(100),
				WeekDetailFlag bit,
				WeekMultipleFlag bit,
				WeekDayName varchar(100),
				WeekDayDetailFlag bit,
				WeekDayMultipleFlag bit,
				DayName varchar(100),
				DayDetailFlag bit,
				DayMultipleFlag bit
				) 
			declare @StoreGroupNameList varchar(1000)
			declare @StoreGroupDetailFlag bit
			declare @StoreGroupMultipleFlag bit
			declare @StoreNameList varchar(1000)
			declare @StoreDetailFlag bit
			declare @StoreMultipleFlag bit
			declare @ProfitCenterNameList varchar(1000)
			declare @ProfitCenterDetailFlag bit
			declare @ProfitCenterMultipleFlag bit
			declare @DayPartNameList varchar(1000)
			declare @DayPartDetailFlag bit
			declare @DayPartMultipleFlag bit
			declare @DayHourNameList varchar(1000)
			declare @DayHourDetailFlag bit
			declare @DayHourMultipleFlag bit
			declare @FrequentDinerPlanNameList varchar(1000)
			declare @FrequentDinerPlanDetailFlag bit
			declare @FrequentDinerPlanMultipleFlag bit
			declare @FrequentDinerCustomerNameList varchar(1000)
			declare @FrequentDinerCustomerDetailFlag bit
			declare @FrequentDinerCustomerMultipleFlag bit
			declare @StoreStaffNameList varchar(1000)
			declare @StoreStaffDetailFlag bit
			declare @StoreStaffMultipleFlag bit
			declare @CheckNumberNameList varchar(1000)
			declare @CheckNumberDetailFlag bit
			declare @CheckNumberMultipleFlag bit
			declare @MainTypeNameList varchar(1000)
			declare @MainTypeDetailFlag bit
			declare @MainTypeMultipleFlag bit
			declare @MainGroupNameList varchar(1000)
			declare @MainGroupDetailFlag bit
			declare @MainGroupMultipleFlag bit
			declare @SaleTypeNameList varchar(1000)
			declare @SaleTypeDetailFlag bit
			declare @SaleTypeMultipleFlag bit
			declare @MenuItemNameList varchar(1000)
			declare @MenuItemDetailFlag bit
			declare @MenuItemMultipleFlag bit
			declare @MenuItemPLUList varchar(1000)
			declare @MenuItemPLUDetailFlag bit
			declare @MenuItemPLUMultipleFlag bit
			declare @DiscountNameList varchar(1000)
			declare @DiscountDetailFlag bit
			declare @DiscountMultipleFlag bit
			declare @SpecialNameList varchar(100)
			declare @SpecialDetailFlag bit
			declare @SpecialMultipleFlag bit
			declare @WeekNameList varchar(100)
			declare @WeekDetailFlag bit
			declare @WeekMultipleFlag bit
			declare @WeekDayNameList varchar(100)
			declare @WeekDayDetailFlag bit
			declare @WeekDayMultipleFlag bit
			declare @DayNameList varchar(100)
			declare @DayDetailFlag bit
			declare @DayMultipleFlag bit

			set @StoreGroupDetailFlag = 0
			set @StoreDetailFlag = 0
			set @ProfitCenterDetailFlag = 0
			set @DayPartDetailFlag = 0
			set @DayHourDetailFlag = 0
			set @FrequentDinerPlanDetailFlag = 0
			set @FrequentDinerCustomerDetailFlag = 0
			set @StoreStaffDetailFlag = 0
			set @CheckNumberDetailFlag = 0
			set @MainTypeDetailFlag = 0
			set @MainGroupDetailFlag = 0
			set @SaleTypeDetailFlag = 0
			set @MenuItemDetailFlag = 0
			set @MenuItemPLUDetailFlag = 0
			set @DiscountDetailFlag = 0
			set @SpecialDetailFlag = 0
			set @WeekDetailFlag = 0
			set @WeekDayDetailFlag = 0
			set @DayDetailFlag = 0

			set @StoreGroupMultipleFlag = 0
			set @StoreMultipleFlag = 0
			set @ProfitCenterMultipleFlag = 0
			set @DayPartMultipleFlag = 0
			set @DayHourMultipleFlag = 0
			set @FrequentDinerPlanMultipleFlag = 0
			set @FrequentDinerCustomerMultipleFlag = 0
			set @StoreStaffMultipleFlag = 0
			set @CheckNumberMultipleFlag = 0
			set @MainTypeMultipleFlag = 0
			set @MainGroupMultipleFlag = 0
			set @SaleTypeMultipleFlag = 0
			set @MenuItemMultipleFlag = 0
			set @MenuItemPLUMultipleFlag = 0
			set @DiscountMultipleFlag = 0
			set @SpecialMultipleFlag = 0
			set @WeekMultipleFlag = 0
			set @WeekDayMultipleFlag = 0
			set @DayMultipleFlag = 0

			set @DayPartNameList = null
			if len(@DayPartListCSV)=0
				begin
				set @DayPartNameList = 'All'
				set @DayPartMultipleFlag = 1
				end
			else
				begin
				declare @DayPart varchar(50)
				declare @DayPartID int
				declare cur cursor for
					select distinct
						DayPart, DayPartID
					from
						dbo.fn_DayPartsTable(0) dpt
					inner join
						dbo.fn_CreateKeyTableValueFromCSVString(@DayPartListCSV) kv
					on kv.KeyValue=dpt.DayPartID
					order by DayPartID
				open cur
				if @@cursor_rows>8
					set @DayPartDetailFlag = 1
				else
					if @@cursor_rows>1
						set @DayPartMultipleFlag=1
				fetch next from cur into @DayPart, @DayPartID
				while @@fetch_status=0
					begin
					if @DayPartNameList is null
						set @DayPartNameList = ''
					else
						if  len(@DayPartNameList)<995
							if @DayPartDetailFlag=1
								set @DayPartNameList = @DayPartNameList + ', '
							else
								set @DayPartNameList = @DayPartNameList + '<br/>'
					set @DayPartNameList = left(@DayPartNameList + rtrim(isnull(@DayPart,'')),1000)
					fetch next from cur into @DayPart, @DayPartID
					end
				close cur
				deallocate cur
				end

			set @DayHourNameList = null
			declare @DayHourNumber int
			declare @DayHourName varchar(50)
			declare @DayHourCount int
			if len(@DayHourListCSV)=0
				begin
				set @DayHourNameList = 'All'
				set @DayHourMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select
						KeyValue as DayHourNumber,
						case
							when KeyValue in (0,24)
								then 'Midnight'
							when KeyValue in (12,36)
								then 'Noon'
							else right('  ' + cast(KeyValue as varchar(3)),2) +
								case when floor(KeyValue/12)%2=1 then ':00 PM' else ':00 AM' end
						end as DayHourName
					from
						fn_CreateKeyTableValueFromCSVString(@DayHourListCSV) csv
					order by KeyValue
				open cur
				if @@cursor_rows>8
					set @DayHourDetailFlag = 1
				else
					if @@cursor_rows>1
						set @DayHourMultipleFlag=1
				fetch next from cur into @DayHourNumber, @DayHourName
				while @@fetch_status=0
					begin
					if @DayHourNameList is null
						set @DayHourNameList = ''
					else
						if  len(@DayHourNameList)<995
							if @DayHourDetailFlag=1
								set @DayHourNameList = @DayHourNameList + ', '
							else
								set @DayHourNameList = @DayHourNameList + '<br/>'
					set @DayHourNameList = left(@DayHourNameList + rtrim(isnull(@DayHourName,'')),1000)
					fetch next from cur into @DayHourNumber, @DayHourName
					end
				close cur
				deallocate cur
				end

			set @DayNameList = null
			declare @DayNumber int
			declare @DayName varchar(50)
			declare @DayCount int
			if len(@DayListCSV)=0
				begin
				set @DayNameList = 'All'
				set @DayMultipleFlag=1
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
						fn_CreateKeyTableValueFromCSVString(@DayListCSV) csv
					order by KeyValue
				open cur
				if @@cursor_rows>8
					set @DayDetailFlag = 1
				else
					if @@cursor_rows>1
						set @DayMultipleFlag=1
				fetch next from cur into @DayNumber, @DayName
				while @@fetch_status=0
					begin
					if @DayNameList is null
						set @DayNameList = ''
					else
						if  len(@DayNameList)<995
							if @DayDetailFlag=1
								set @DayNameList = @DayNameList + ', '
							else
								set @DayNameList = @DayNameList + '<br/>'
					set @DayNameList = left(@DayNameList + rtrim(isnull(@DayName,'')),1000)
					fetch next from cur into @DayNumber, @DayName
					end
				close cur
				deallocate cur
				end

			set @WeekDayNameList = null
			declare @WeekDayNumber int
			declare @WeekDayName varchar(50)
			declare @WeekDayCount int
			if len(@WeekDayListCSV)=0
				begin
				set @WeekDayNameList = 'All'
				set @WeekDayMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select distinct
						KeyValue as WeekDayNumber,
						datename(dw,dbo.fn_CalcDayNumberDate(KeyValue)) as WeekDayName
					from
						fn_CreateKeyTableValueFromCSVString(@WeekDayListCSV) csv
					order by KeyValue
				open cur
				if @@cursor_rows>8
					set @WeekDayDetailFlag = 1
				else
					if @@cursor_rows>1
						set @WeekDayMultipleFlag=1
				fetch next from cur into @WeekDayNumber, @WeekDayName
				while @@fetch_status=0
					begin
					if @WeekDayNameList is null
						set @WeekDayNameList = ''
					else
						if  len(@WeekDayNameList)<995
							if @WeekDayDetailFlag=1
								set @WeekDayNameList = @WeekDayNameList + ', '
							else
								set @WeekDayNameList = @WeekDayNameList + '<br/>'
					set @WeekDayNameList = left(@WeekDayNameList + rtrim(isnull(@WeekDayName,'')),1000)
					fetch next from cur into @WeekDayNumber, @WeekDayName
					end
				close cur
				deallocate cur
				end

			set @WeekNameList = null
			declare @WeekNumber int
			declare @WeekName varchar(50)
			declare @WeekCount int
			if len(@WeekListCSV)=0
				begin
				set @WeekNameList = 'All'
				set @WeekMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select distinct
						KeyValue as WeekNumber,
						left(datename(mm, dbo.fn_CalcDayNumberDate(floor(KeyValue/7))),3) + ' '
							+ datename(dd, dbo.fn_CalcDayNumberDate(floor(KeyValue/7))) + ', '
							+ datename(yyyy, dbo.fn_CalcDayNumberDate(floor(KeyValue/7)))
						as WeekName
					from
						fn_CreateKeyTableValueFromCSVString(@WeekListCSV) csv
					order by KeyValue
				open cur
				if @@cursor_rows>8
					set @WeekDetailFlag = 1
				else
					if @@cursor_rows>1
						set @WeekMultipleFlag=1
				fetch next from cur into @WeekNumber, @WeekName
				while @@fetch_status=0
					begin
					if @WeekNameList is null
						set @WeekNameList = ''
					else
						if  len(@WeekNameList)<995
							if @WeekDetailFlag=1
								set @WeekNameList = @WeekNameList + ', '
							else
								set @WeekNameList = @WeekNameList + '<br/>'
					set @WeekNameList = left(@WeekNameList + rtrim(isnull(@WeekName,'')),1000)
					fetch next from cur into @WeekNumber, @WeekName
					end
				close cur
				deallocate cur
				end

			set @StoreGroupNameList = null
			declare @StoreGroupID int
			declare @StoreGroupName varchar(50)
			declare @StoreGroupDisplayOrder int
			declare @StoreGroupCount int
			if len(@StoreGroupListCSV)=0
				begin
				set @StoreGroupNameList = 'All'
				set @StoreGroupMultipleFlag=1
				end
			else
				begin
				declare cur cursor for
					select
						KeyValue as StoreGroupID,
						tblStoreGroups.StoreGroupName,
						tblStoreGroups.DisplayOrder as StoreGroupDisplayOrder
					from
						tblStoreGroups
					inner join
						fn_CreateKeyTableValueFromCSVString(@StoreGroupListCSV) csv
					on csv.KeyValue=tblStoreGroups.StoreGroupID
					order by tblStoreGroups.DisplayOrder
				open cur
				if @@cursor_rows>8
					set @StoreGroupDetailFlag = 1
				else
					if @@cursor_rows>1
						set @StoreGroupMultipleFlag=1
				fetch next from cur into @StoreGroupID, @StoreGroupName, @StoreGroupDisplayOrder
				while @@fetch_status=0
					begin
					if @StoreGroupNameList is null
						set @StoreGroupNameList = ''
					else
						if  len(@StoreGroupNameList)<995
							if @StoreGroupDetailFlag=1
								set @StoreGroupNameList = @StoreGroupNameList + ', '
							else
								set @StoreGroupNameList = @StoreGroupNameList + '<br/>'
					set @StoreGroupNameList = left(@StoreGroupNameList + rtrim(isnull(@StoreGroupName,'')),1000)
					fetch next from cur into @StoreGroupID, @StoreGroupName, @StoreGroupDisplayOrder
					end
				close cur
				deallocate cur
				end
			declare @tblStores table(StoreID int, StoreName varchar(50), StoreGCode varchar(50), StoreNumber int)
			if len(@StoreListCSV)>0
				insert into @tblStores
				select
					KeyValue, isnull(tblStores.StoreReportName, tblStores.StoreName) as StoreName, tblStores.StoreGCode, tblStores.StoreNumber
				from
					tblStores
				inner join
					fn_CreateKeyTableValueFromCSVString(@StoreListCSV) csv
				on csv.KeyValue=tblStores.StoreID

			set @StoreNameList = null
			declare @StoreID int
			declare @StoreName varchar(50)
			declare @StoreNumber int
			declare @StoreGCode varchar(50)
			if len(@StoreListCSV)=0
				begin
				set @StoreNameList = 'All'
				set @StoreMultipleFlag=1
				end
			else
				begin
				declare cur cursor for select distinct StoreID, StoreName, StoreGCode, StoreNumber from @tblStores order by StoreNumber
				open cur
				if @@cursor_rows>8
					set @StoreDetailFlag = 1
				else
					if @@cursor_rows>1
						set @StoreMultipleFlag=1
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

			set @ProfitCenterNameList = null
			if len(@ProfitCenterListCSV)=0
				begin
				set @ProfitCenterNameList = 'All'
				set @ProfitCenterMultipleFlag = 1
				end
			else
				begin
				declare @ProfitCenter varchar(50)
				declare cur cursor for
					select ProfitCenter from dbo.fn_ParseSelectedProfitCenters(@ProfitCenterListCSV) pc
				open cur
				if @@cursor_rows>8
					set @ProfitCenterDetailFlag = 1
				else
					if @@cursor_rows>1
						set @ProfitCenterMultipleFlag=1
				fetch next from cur into @ProfitCenter
				while @@fetch_status=0
					begin
					if @ProfitCenterNameList is null
						set @ProfitCenterNameList = ''
					else
						if  len(@ProfitCenterNameList)<995
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

			set @FrequentDinerPlanNameList = null
			if len(@FrequentDinerPlanListCSV)=0
				begin
				set @FrequentDinerPlanNameList = 'All'
				set @FrequentDinerPlanMultipleFlag=1
				end
			else
				begin
				declare @FrequentDinerPlan varchar(50)
				declare cur cursor for
					select FrequentDinerPlan from dbo.fn_ParseSelectedFrequentDinerPlans(@FrequentDinerPlanListCSV) pc
				open cur
				if @@cursor_rows>8
					set @FrequentDinerPlanDetailFlag = 1
				else
					if @@cursor_rows>1
						set @FrequentDinerPlanMultipleFlag=1
				fetch next from cur into @FrequentDinerPlan
				while @@fetch_status=0
					begin
					if @FrequentDinerPlanNameList is null
						set @FrequentDinerPlanNameList = ''
					else
						if  len(@FrequentDinerPlanNameList)<995
							if @FrequentDinerPlanDetailFlag=1
								set @FrequentDinerPlanNameList = @FrequentDinerPlanNameList + ', '
							else
								set @FrequentDinerPlanNameList = @FrequentDinerPlanNameList + '<br/>'
					set @FrequentDinerPlanNameList = left(@FrequentDinerPlanNameList + rtrim(isnull(@FrequentDinerPlan,'')),1000)
					fetch next from cur into @FrequentDinerPlan
					end
				close cur
				deallocate cur
				end

			set @StoreStaffNameList = null
			if len(@StoreStaffListCSV)=0
				begin
				set @StoreStaffNameList = 'All'
				set @StoreStaffMultipleFlag=1
				end
			else
				begin
				declare @StoreStaff varchar(50)
				declare cur cursor for
					select StoreStaff from dbo.fn_ParseSelectedStoreStaff(@StoreStaffListCSV) pc
				open cur
				if @@cursor_rows>8
					set @StoreStaffDetailFlag = 1
				else
					if @@cursor_rows>1
						set @StoreStaffMultipleFlag=1
				fetch next from cur into @StoreStaff
				while @@fetch_status=0
					begin
					if @StoreStaffNameList is null
						set @StoreStaffNameList = ''
					else
						if  len(@StoreStaffNameList)<995
							if @StoreStaffDetailFlag=1
								set @StoreStaffNameList = @StoreStaffNameList + ', '
							else
								set @StoreStaffNameList = @StoreStaffNameList + '<br/>'
					set @StoreStaffNameList = left(@StoreStaffNameList + rtrim(isnull(@StoreStaff,'')),1000)
					fetch next from cur into @StoreStaff
					end
				close cur
				deallocate cur
				end

			set @FrequentDinerCustomerNameList = null
			if len(@FrequentDinerCustomerListCSV)=0
				begin
				set @FrequentDinerCustomerNameList = 'All'
				set @FrequentDinerCustomerMultipleFlag=1
				end
			else
				begin
				declare @FrequentDinerCustomer varchar(50)
				declare cur cursor for
					select FrequentDinerCustomer from dbo.fn_ParseSelectedFrequentDinerCustomers(@FrequentDinerCustomerListCSV) pc
				open cur
				if @@cursor_rows>8
					set @FrequentDinerCustomerDetailFlag = 1
				else
					if @@cursor_rows>1
						set @FrequentDinerCustomerMultipleFlag=1
				fetch next from cur into @FrequentDinerCustomer
				while @@fetch_status=0
					begin
					if @FrequentDinerCustomerNameList is null
						set @FrequentDinerCustomerNameList = ''
					else
						if len(@FrequentDinerCustomerNameList)<995
							if @FrequentDinerCustomerDetailFlag=1
								set @FrequentDinerCustomerNameList = @FrequentDinerCustomerNameList + ', '
							else
								set @FrequentDinerCustomerNameList = @FrequentDinerCustomerNameList + '<br/>'
					set @FrequentDinerCustomerNameList = left(@FrequentDinerCustomerNameList + rtrim(isnull(@FrequentDinerCustomer,'')),1000)
					fetch next from cur into @FrequentDinerCustomer
					end
				close cur
				deallocate cur
				end

			declare @tblMainTypes table(MainTypeID int, MainType varchar(50))
			if len(@MainTypeListCSV)>0
				insert into @tblMainTypes
				select
					MainTypeID,
					Description as MainType
				from
					tblMainTypes
				inner join 
					fn_CreateKeyTableValueFromCSVString(@MainTypeListCSV) csv
				on csv.KeyValue=tblMainTypes.MainTypeID

			set @MainTypeNameList = null
			if len(@MainTypeListCSV)=0
				begin
				set @MainTypeNameList = 'All'
				set @MainTypeMultipleFlag=1
				end
			else
				begin
				declare @MainTypeID int
				declare @MainType varchar(50)
				declare cur cursor for
					select MainTypeID, MainType
					from @tblMainTypes
				open cur
				if @@cursor_rows>8
					set @MainTypeDetailFlag = 1
				else
					if @@cursor_rows>1
						set @MainTypeMultipleFlag=1
				fetch next from cur into @MainTypeID, @MainType
				while @@fetch_status=0
					begin
					if @MainTypeNameList is null
						set @MainTypeNameList = ''
					else
						if  len(@MainTypeNameList)<995
							if @MainTypeDetailFlag=1
								set @MainTypeNameList = @MainTypeNameList + ', '
							else
								set @MainTypeNameList = @MainTypeNameList + '<br/>'
					set @MainTypeNameList = left(@MainTypeNameList + rtrim(isnull(@MainType,'')),1000)
					fetch next from cur into @MainTypeID, @MainType
					end
				close cur
				deallocate cur
				end

			set @MainGroupNameList = null
			if len(@MainGroupListCSV)=0
				begin
				set @MainGroupNameList = 'All'
				set @MainGroupMultipleFlag=1
				end
			else
				begin
				declare @MainGroup varchar(50)
				declare cur cursor for
					select MainGroup from dbo.fn_ParseSelectedMainGroups(@MainGroupListCSV) pc
				open cur
				if @@cursor_rows>8
					set @MainGroupDetailFlag = 1
				else
					if @@cursor_rows>1
						set @MainGroupMultipleFlag=1
				fetch next from cur into @MainGroup
				while @@fetch_status=0
					begin
					if @MainGroupNameList is null
						set @MainGroupNameList = ''
					else
						if  len(@MainGroupNameList)<995
							if @MainGroupDetailFlag=1
								set @MainGroupNameList = @MainGroupNameList + ', '
							else
								set @MainGroupNameList = @MainGroupNameList + '<br/>'
					set @MainGroupNameList = left(@MainGroupNameList + rtrim(isnull(@MainGroup,'')),1000)
					fetch next from cur into @MainGroup
					end
				close cur
				deallocate cur
				end

			set @SaleTypeNameList = null
			if len(@SaleTypeListCSV)=0
				begin
				set @SaleTypeNameList = 'All'
				set @SaleTypeMultipleFlag=1
				end
			else
				begin
				declare @SaleType varchar(50)
				declare cur cursor for
					select SaleType from dbo.fn_ParseSelectedSaleTypes(@SaleTypeListCSV) pc
				open cur
				if @@cursor_rows>8
					set @SaleTypeDetailFlag = 1
				else
					if @@cursor_rows>1
						set @SaleTypeMultipleFlag=1
				fetch next from cur into @SaleType
				while @@fetch_status=0
					begin
					if @SaleTypeNameList is null
						set @SaleTypeNameList = ''
					else
						if  len(@SaleTypeNameList)<995
							if @SaleTypeDetailFlag=1
								set @SaleTypeNameList = @SaleTypeNameList + ', '
							else
								set @SaleTypeNameList = @SaleTypeNameList + '<br/>'
					set @SaleTypeNameList = left(@SaleTypeNameList + rtrim(isnull(@SaleType,'')),1000)
					fetch next from cur into @SaleType
					end
				close cur
				deallocate cur
				end

			set @MenuItemNameList = null
			if len(@MenuItemListCSV)=0
				begin
				set @MenuItemNameList = 'All'
				set @MenuItemMultipleFlag=1
				end
			else
				begin
				declare @MenuItem varchar(50)
				declare cur cursor for
					select MenuItem from dbo.fn_ParseSelectedMenuItems(@MenuItemListCSV) pc
				open cur
				if @@cursor_rows>8
					set @MenuItemDetailFlag = 1
				else
					if @@cursor_rows>1
						set @MenuItemMultipleFlag=1
				fetch next from cur into @MenuItem
				while @@fetch_status=0
					begin
					if @MenuItemNameList is null
						set @MenuItemNameList = ''
					else
						if  len(@MenuItemNameList)<995
							if @MenuItemDetailFlag=1
								set @MenuItemNameList = @MenuItemNameList + ', '
							else
								set @MenuItemNameList = @MenuItemNameList + '<br/>'
					set @MenuItemNameList = left(@MenuItemNameList + rtrim(isnull(@MenuItem,'')),1000)
					fetch next from cur into @MenuItem
					end
				close cur
				deallocate cur
				end

			set @MenuItemPLUList = null
			if len(@MenuItemPLUListCSV)=0
				begin
				set @MenuItemPLUList = 'All'
				set @MenuItemPLUMultipleFlag=1
				end
			else
				begin
				declare @MenuItemPLU int
				declare cur cursor for
					select MenuItemID as MenuItemPLU from dbo.fn_ParseSelectedMenuItemPLUs(@MenuItemPLUListCSV) pc
				open cur
				if @@cursor_rows>8
					set @MenuItemPLUDetailFlag = 1
				else
					if @@cursor_rows>1
						set @MenuItemPLUMultipleFlag=1
				fetch next from cur into @MenuItemPLU
				while @@fetch_status=0
					begin
					if @MenuItemPLUList is null
						set @MenuItemPLUList = ''
					else
						if  len(@MenuItemPLUList)<995
							if @MenuItemDetailFlag=1
								set @MenuItemPLUList = @MenuItemPLUList + ', '
							else
								set @MenuItemPLUList = @MenuItemPLUList + '<br/>'
					set @MenuItemPLUList = left(@MenuItemPLUList + rtrim(isnull(@MenuItemPLU,'')),1000)
					fetch next from cur into @MenuItemPLU
					end
				close cur
				deallocate cur
				end

			set @DiscountNameList = null
			if len(@DiscountListCSV)=0
				begin
				set @DiscountNameList = 'All'
				set @DiscountMultipleFlag=1
				end
			else
				begin
				declare @Discount varchar(50)
				declare cur cursor for
					select Discount from dbo.fn_ParseSelectedDiscounts(@DiscountListCSV) pc
				open cur
				if @@cursor_rows>8
					set @DiscountDetailFlag = 1
				else
					if @@cursor_rows>1
						set @DiscountMultipleFlag=1
				fetch next from cur into @Discount
				while @@fetch_status=0
					begin
					if @DiscountNameList is null
						set @DiscountNameList = ''
					else
						if  len(@DiscountNameList)<995
							if @DiscountDetailFlag=1
								set @DiscountNameList = @DiscountNameList + ', '
							else
								set @DiscountNameList = @DiscountNameList + '<br/>'
					set @DiscountNameList = left(@DiscountNameList + rtrim(isnull(@Discount,'')),1000)
					fetch next from cur into @Discount
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
						ItemID as KeyValue,
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

			set @SpecialNameList = 
				case
					when len(@SpecialListCSV)=0 then ''
					when @SpecialListCSV='0' then '<B>Non-Specials Only</B>'
					when @SpecialListCSV='1' then '<B>Specials Only</B>'
					else ''
				end

			INSERT INTO @table_variable (
					StoreGroup, StoreName, ProfitCenter, MainType, MainGroup, SaleType, MenuItem, MenuItemPLU, Discount, Special, DayPart, DayHourName, WeekName, WeekDayName, DayName, FrequentDinerPlan, FrequentDinerCustomer, StoreStaff, CheckNumber,
					StoreGroupDetailFlag, StoreDetailFlag, ProfitCenterDetailFlag, MainTypeDetailFlag, MainGroupDetailFlag, SaleTypeDetailFlag, MenuItemDetailFlag, MenuItemPLUDetailFlag, DiscountDetailFlag, SpecialDetailFlag, DayPartDetailFlag, DayHourDetailFlag, WeekDetailFlag, WeekDayDetailFlag, DayDetailFlag, FrequentDinerPlanDetailFlag, FrequentDinerCustomerDetailFlag, StoreStaffDetailFlag, CheckNumberDetailFlag,
					StoreGroupMultipleFlag, StoreMultipleFlag, ProfitCenterMultipleFlag, MainTypeMultipleFlag, MainGroupMultipleFlag, SaleTypeMultipleFlag, MenuItemMultipleFlag, MenuItemPLUMultipleFlag, DiscountMultipleFlag, SpecialMultipleFlag, DayPartMultipleFlag, DayHourMultipleFlag, WeekMultipleFlag, WeekDayMultipleFlag, DayMultipleFlag, FrequentDinerPlanMultipleFlag, FrequentDinerCustomerMultipleFlag, StoreStaffMultipleFlag, CheckNumberMultipleFlag
					) 
				values (
					@StoreGroupNameList, @StoreNameList, @ProfitCenterNameList, @MainTypeNameList, @MainGroupNameList, @SaleTypeNameList, @MenuItemNameList, @MenuItemPLUList, @DiscountNameList, @SpecialNameList, @DayPartNameList, @DayHourNameList, @WeekNameList, @WeekDayNameList, @DayNameList, @FrequentDinerPlanNameList, @FrequentDinerCustomerNameList, @StoreStaffNameList, @CheckNumberNameList,
					@StoreGroupDetailFlag, @StoreDetailFlag, @ProfitCenterDetailFlag, @MainTypeDetailFlag, @MainGroupDetailFlag, @SaleTypeDetailFlag, @MenuItemDetailFlag, @MenuItemPLUDetailFlag, @DiscountDetailFlag, @SpecialDetailFlag, @DayPartDetailFlag, @DayHourDetailFlag, @WeekDetailFlag, @WeekDayDetailFlag, @DayDetailFlag, @FrequentDinerPlanDetailFlag, @FrequentDinerCustomerDetailFlag, @StoreStaffDetailFlag, @CheckNumberDetailFlag,
					@StoreGroupMultipleFlag, @StoreMultipleFlag, @ProfitCenterMultipleFlag, @MainTypeMultipleFlag, @MainGroupMultipleFlag, @SaleTypeMultipleFlag, @MenuItemMultipleFlag, @MenuItemPLUMultipleFlag, @DiscountMultipleFlag, @SpecialMultipleFlag, @DayPartMultipleFlag, @DayHourMultipleFlag, @WeekMultipleFlag, @WeekDayMultipleFlag, @DayMultipleFlag, @FrequentDinerPlanMultipleFlag, @FrequentDinerCustomerMultipleFlag, @StoreStaffMultipleFlag, @CheckNumberMultipleFlag
					)");
		strSQLHeader.Append(@"		
			select * from @table_variable");
			SQLHeader = strSQLHeader.ToString();
			return (SQLHeader);
			}
		}
	}
