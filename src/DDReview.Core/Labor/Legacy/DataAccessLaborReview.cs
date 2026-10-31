#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using DDReview.Core.Sales.Legacy;

namespace DDReview.Core.Labor.Legacy {
	public class DataAccessLaborReview : DataAccessReview {
		public int StartDayNumber;
		public int EndDayNumber;
		public int WeekNumberFlag = 0;
		public int StoreGroupFlag = 0;
		public string StoreGroupListCSV = "";
		public int StoreFlag = 1;
		public string StoreListCSV = "";
		public int LaborTypeFlag = 0;
		public string LaborTypeListCSV = "";
		public int LaborGroupFlag = 0;
		public string LaborGroupListCSV = "";
		public int LaborDepartmentFlag = 0;
		public string LaborDepartmentListCSV = "";
		public int EmployeeFlag = 0;
		public string EmployeeListCSV = "";
		public int PayrollIDFlag = 0;
		public string PayrollIDListCSV = "";
		public int DayHourSlotFlag = 0;
		public string DayHourSlotListCSV = "";
		public int DayTimeSlotFlag = 0;
		public string DayTimeSlotListCSV = "";
		public int EmployeeDetailFlag = 0;


		public int RowsReturned = 0;

		public string ConnectionString = "";
		public string SQL = "";
		public string SQLHeader = "";
		public string DefaultSort = "";
		public string BaseSort = "";

		public bool FillDataSet = true;

		public DataAccessLaborReview(string strConnection) {
			ConnectionString = strConnection;
			}

		
		public String BuildDataScript() {
			StringBuilder sb = new StringBuilder();
			String strRateSQLComplete = "";
			String strRateSQL = "";
			Boolean ExpandedTimeSlots = ((DayTimeSlotFlag!=0 || DayTimeSlotListCSV.Length>0 || DayHourSlotFlag!=0 || DayHourSlotListCSV.Length>0));
			sb.Append(@"
					declare @StaffRates table (
						StoreID int,
						StaffDepartmentID int,
						StoreStaffID int,
						HourlyRate money
						)");
			if (strRateSQL.Length==0)
				strRateSQLComplete = @"
					insert into @StaffRates
						select
							StoreID,
							StaffDepartmentID,
							StoreStaffID,
							HourlyRate as HourlyRate
						from
							dbo.fn_StaffRates() tblStaffRates";
			else
				strRateSQLComplete = @"
					declare @PayPeriodEndingDate datetime
					declare @PayrollGroupNumber int
					set @PayPeriodEndingDate = dbo.fn_CalcWeekDayNumberEndDate(@EndWeekNumber)
					set @PayrollGroupNumber = 8191 -- (select min(PayrollGroupNumber) from tblStores where Active<>0)
					insert into @StaffRates
						select
							StoreID,
							StaffDepartmentID,
							StoreStaffID,
							max(case when em.HourlyRate is null then 0 else em.HourlyRate end) as HourlyRate
						from (
							select
								tsr.StoreID,
								tsr.StoreStaffID,
								tsr.StaffDepartmentID,
								tsr.HourlyRate as HourlyRate,
								case when ts.PayrollID=0 then ts.StoreID*100000 + ts.StoreStaffID else ts.PayrollID end as PayrollID,
								sd.JobCode
							from
								dbo.fn_Staff3(@PayrollGroupNumber) ts
							inner join
								dbo.fn_StaffRates() tsr
							on ts.StoreID=tsr.StoreID and ts.StoreStaffID=tsr.StoreStaffID
							inner join
								dbo.fn_StaffDepartments() sd
							on sd.StoreID=tsr.StoreID and sd.StaffDepartmentID=tsr.StaffDepartmentID
							cross join
								tblGlobal
							where (case when ts.PayrollID=0 then ts.StoreID*100000 + ts.StoreStaffID else ts.PayrollID end)%@BigNumber between tblGlobal.PayrollIDLowerLimit and tblGlobal.PayrollIDUpperLimit
							) pm
						left outer join (
							" + strRateSQL + @"
							) em
						on em.PayrollID=pm.PayrollID and em.JobCode=pm.JobCode
						where not pm.PayrollID is null
						group by
							StoreID,
							StoreStaffID,
							StaffDepartmentID
						order by
							StoreID,
							StoreStaffID,
							StaffDepartmentID";
			sb.Append(@"
				declare @StartDayNumber int
				declare @EndDayNumber int
				declare @WeekNumberFlag int
				declare @StoreGroupFlag bit
				declare @StoreGroupListCSV varchar(8000)
				declare @StoreFlag bit
				declare @StoreListCSV varchar(8000)
				declare @TypeFlag bit
				declare @TypeListCSV varchar(8000)
				declare @GroupFlag bit
				declare @GroupListCSV varchar(8000)
				declare @DepartmentFlag bit
				declare @DepartmentListCSV varchar(8000)
				declare @EmployeeFlag bit
				declare @EmployeeListCSV varchar(8000)
				declare @PayrollIDFlag bit
				declare @PayrollIDListCSV varchar(8000)
				declare @DayHourSlotFlag bit
				declare @DayHourSlotListCSV varchar(8000)
				declare @DayTimeSlotFlag bit
				declare @DayTimeSlotListCSV varchar(8000)
				declare @EmployeeDetailFlag bit
				declare @nn bigint = 1895185021
				declare @nm bigint = 1508113067
			");
			sb.Append( 
        @"set @WeekNumberFlag=" + WeekNumberFlag.ToString()+"\n"
				+ @"set @StartDayNumber=" + (StartDayNumber.ToString())+"\n"
				+ @"set @EndDayNumber=" + (EndDayNumber.ToString())+"\n"
        + @"set @StoreGroupFlag=" + StoreGroupFlag.ToString()+"\n"
				+ @"set @StoreGroupListCSV='" + StoreGroupListCSV.ToString()+"'\n"
        + @"set @StoreFlag=" + StoreFlag.ToString()+"\n"
				+ @"set @StoreListCSV='" + StoreListCSV.ToString()+"'\n"
				+ @"set @TypeFlag=" + LaborTypeFlag.ToString()+"\n"
				+ @"set @TypeListCSV='" + LaborTypeListCSV.ToString()+"'\n"
				+ @"set @GroupFlag=" + LaborGroupFlag.ToString()+"\n"
				+ @"set @GroupListCSV='" + LaborGroupListCSV.ToString()+"'\n"
				+ @"set @DepartmentFlag=" + LaborDepartmentFlag.ToString()+"\n"
				+ @"set @DepartmentListCSV='" + LaborDepartmentListCSV.ToString()+"'\n"
				+ @"set @EmployeeFlag=" + EmployeeFlag.ToString()+"\n"
				+ @"set @EmployeeListCSV='" + EmployeeListCSV.ToString()+"'\n"
				+ @"set @PayrollIDFlag=" + PayrollIDFlag.ToString()+"\n"
				+ @"set @PayrollIDListCSV='" + PayrollIDListCSV.ToString()+"'\n"
				+ @"set @DayHourSlotFlag=" + DayHourSlotFlag.ToString()+"\n"
				+ @"set @DayHourSlotListCSV='" + DayHourSlotListCSV.ToString()+"'\n"
				+ @"set @DayTimeSlotFlag=" + DayTimeSlotFlag.ToString()+"\n"
				+ @"set @DayTimeSlotListCSV='" + DayTimeSlotListCSV.ToString()+"'\n"
				+ @"set @EmployeeDetailFlag=" + EmployeeDetailFlag.ToString()+"\n");
			sb.Append(strRateSQLComplete+"\n\n");
			sb.Append(@"
					declare @StartDate datetime
					declare @EndDate datetime
					set @StartDate = dbo.fn_CalcDayNumberDate(@StartDayNumber)
					set @EndDate = dbo.fn_CalcDayNumberDate(@EndDayNumber)");
			sb.Append(@"
			declare @PayrollIDLowerLimit bigint
			declare @PayrollIDUpperLimit bigint
			declare @BigNumber bigint
			declare @PayrollGroupNumber bigint
			declare @PayrollBigNumber bigint

			set @PayrollIDLowerLimit = (select PayrollIDLowerLimit from tblGlobal)
			set @PayrollIDUpperLimit = (select PayrollIDUpperLimit from tblGlobal)+1
			set @BigNumber = 1000000000000000

			declare @PayrollWeeklyOvertimeProrated money
			declare @PayrollWeeklyOvertimeChronological money
			set @PayrollWeeklyOvertimeProrated = case when (select top 1 PayrollOvertimeChronological from tblGlobal)<>0 then 0.0 else 1.0 end
			set @PayrollWeeklyOvertimeChronological = case when (select top 1 PayrollOvertimeChronological from tblGlobal)<>0 then 1.0 else 0.0 end

			declare @GroupSelectTable table(KeyValue int)
			declare @DepartmentSelectTable table (KeyValue int, StoreID int, JobCode int)
			declare @EmployeeSelectTable table (KeyValue bigint, EmployeeName varchar(255))
			declare @PayrollIDSelectTable table (KeyValue bigint, PayrollIDName varchar(102))

 			declare @tblStoreGroups table(StoreGroupID int, StoreGroupName varchar(60), StoreGroupNumber int)
			if len(@StoreGroupListCSV)>0
				insert into @tblStoreGroups select KeyValue as StoreGroupID, tblStoreGroups.StoreGroupName, tblStoreGroups.StoreGroupNumber from fn_CreateKeyTableValueFromCSVString(@StoreGroupListCSV) csv inner join tblStoreGroups on csv.KeyValue=tblStoreGroups.StoreGroupID
			else 
				insert into @tblStoreGroups(StoreGroupID, StoreGroupName, StoreGroupNumber)
				select distinct StoreGroupID, StoreGroupName, StoreGroupNumber from tblStoreGroups

 			declare @tblStores table(StoreID int, StoreName varchar(50), StoreGCode varchar(50), StoreNumber int, StoreGroupID int, PayrollGroupNumber bigint)
			if len(@StoreListCSV)>0
				insert into @tblStores select KeyValue as StoreID, isnull(tblStores.StoreReportName, tblStores.StoreName) as StoreName, tblStores.StoreGCode, tblStores.StoreNumber, tblStores.StoreGroupID, PayrollGroupNumber from fn_CreateKeyTableValueFromCSVString(@StoreListCSV) csv inner join tblStores on csv.KeyValue=tblStores.StoreID
			else 
				insert into @tblStores(StoreID, StoreName, StoreGCode, StoreNumber, StoreGroupID, PayrollGroupNumber)
				select distinct StoreID, isnull(tblStores.StoreReportName, tblStores.StoreName) as StoreName, StoreGCode, StoreNumber, StoreGroupID, PayrollGroupNumber from tblStores where Active=1

			set @PayrollGroupNumber=8191
			set @PayrollBigNumber = @PayrollGroupNumber*@BigNumber

			declare @tblStaffDepartmentTypes table(StoreID int, StaffDepartmentTypeID int, StaffDepartmentType varchar(50), StaffDepartmentTypeSort varchar(6))
			if len(@TypeListCSV)>0
				insert into @tblStaffDepartmentTypes
				select distinct
					sdg.StoreID,
					StaffDepartmentTypeID,
					StaffDepartmentType,
					StaffDepartmentTypeSort
				from
					tblStaffDepartmentGroups sdg
				inner join
					@tblStores tblStores
				on sdg.StoreID=tblStores.StoreID
				inner join
					fn_CreateKeyTableValueFromCSVString(@TypeListCSV) csv
				on csv.KeyValue=sdg.StaffDepartmentTypeID
			else
				insert into @tblStaffDepartmentTypes
				select distinct
					sdg.StoreID,
					StaffDepartmentTypeID,
					StaffDepartmentType,
					StaffDepartmentTypeSort
				from
					tblStaffDepartmentGroups sdg
				inner join
					@tblStores sst
				on sdg.StoreID=sst.StoreID

			declare @tblStaffDepartmentGroups table(StoreID int, StaffDepartmentGroupID int, StaffDepartmentGroup varchar(50), StaffDepartmentGroupSort varchar(6), StaffDepartmentTypeID int)
			if len(@GroupListCSV)>0
				insert into @tblStaffDepartmentGroups
				select distinct
					sdg.StoreID,
					StaffDepartmentGroupID,
					StaffDepartmentGroup,
					StaffDepartmentGroupSort,
					StaffDepartmentTypeID
				from
					tblStaffDepartmentGroups sdg
				inner join
					@tblStores sst
				on sdg.StoreID=sst.StoreID");
			if (LaborGroupListCSV.Length>0)
				sb.Append ("\n\t\t\t\twhere (" + ParsePairSetOutput(LaborGroupListCSV,"sdg.StoreID","sdg.StaffDepartmentGroupID") + ")");
			sb.Append (@"
			else
				insert into @tblStaffDepartmentGroups
				select distinct
					sdg.StoreID,
					StaffDepartmentGroupID,
					StaffDepartmentGroup,
					StaffDepartmentGroupSort,
					StaffDepartmentTypeID
				from
					tblStaffDepartmentGroups sdg
				inner join
					@tblStores sst
				on sdg.StoreID=sst.StoreID

			declare @tblStaffDepartments table(StoreID int, StaffDepartmentID int, StaffDepartmentName varchar(50), StaffDepartmentSort varchar(6), StaffDepartmentAbbreviation varchar(10), JobCode int, AccruesOvertime int, StaffDepartmentGroupID int)
			if len(@DepartmentListCSV)>0
				insert into @tblStaffDepartments
				select
					tblStaffDepartments.StoreID,
					tblStaffDepartments.StaffDepartmentID,
					tblStaffDepartments.StaffDepartmentName,
					tblStaffDepartments.StaffDepartmentSort,
					tblStaffDepartments.StaffDepartmentAbbreviation,
					tblStaffDepartments.JobCode,
					tblStaffDepartments.AccruesOvertime,
					tblStaffDepartments.StaffDepartmentGroupID
				from
					dbo.fn_StaffDepartments() tblStaffDepartments
				inner join
					@tblStores tblStores
				on tblStores.StoreID=tblStaffDepartments.StoreID");
			if (LaborDepartmentListCSV.Length>0)
				sb.Append ("\n\t\t\t\twhere (" + ParsePairSetOutput(LaborDepartmentListCSV,"tblStaffDepartments.StoreID","tblStaffDepartments.StaffDepartmentID") + ")");
			sb.Append (@"
			else
				insert into @tblStaffDepartments
				select
					tblStaffDepartments.StoreID,
					tblStaffDepartments.StaffDepartmentID,
					tblStaffDepartments.StaffDepartmentName,
					tblStaffDepartments.StaffDepartmentSort,
					tblStaffDepartments.StaffDepartmentAbbreviation,
					tblStaffDepartments.JobCode,
					tblStaffDepartments.AccruesOvertime,
					tblStaffDepartments.StaffDepartmentGroupID
				from
					dbo.fn_StaffDepartments() tblStaffDepartments
				inner join
					@tblStores tblStores
				on tblStores.StoreID=tblStaffDepartments.StoreID

			if len(@EmployeeListCSV)>0
				insert into @EmployeeSelectTable
					select
						@PayrollBigNumber+csv.KeyValue as KeyValue,
						qem.EmployeeName
					from
						fn_CreateBigIntKeyTableValueFromCSVString(@EmployeeListCSV) csv
					inner join (
						select
							case when tblStaff.PayrollID=0 then @PayrollBigNumber+tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end as PayrollID,
							max(isnull(rtrim(tblStaff.LastName)+', ','')+isnull(rtrim(tblStaff.FirstName),'')) as EmployeeName
						from
							dbo.fn_Staff3(@PayrollGroupNumber) tblStaff
						inner join
							@tblStores sst
						on tblStaff.StoreID=sst.StoreID
						group by case when tblStaff.PayrollID=0 then @PayrollBigNumber+tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end
						) qem
					on qem.PayrollID%@BigNumber=csv.KeyValue
			else
				begin
				insert into @EmployeeSelectTable
					select
						case when tblStaff.PayrollID=0 then tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end as KeyValue,
						max(isnull(rtrim(tblStaff.LastName)+', ','')+isnull(rtrim(tblStaff.FirstName),'')) as EmployeeName
					from
						dbo.fn_Staff3(@PayrollGroupNumber) tblStaff
					inner join
						@tblStores sst
					on tblStaff.StoreID=sst.StoreID
					group by case when tblStaff.PayrollID=0 then tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end
				insert into @EmployeeSelectTable (KeyValue, EmployeeName) values (99000, 'Management Overhead')
				end

			if len(@PayrollIDListCSV)>0
				insert into @PayrollIDSelectTable
					select
						csv.KeyValue,
						qem.PayrollIDName
					from
						fn_CreateBigIntKeyTableValueFromCSVString(@PayrollIDListCSV) csv
					inner join (
						select
							PayrollID,
							FullName as PayrollIDName
						from
							dbo.fn_StaffByPayrollID3(@PayrollGroupNumber) tblStaff
						) qem
					on qem.PayrollID=csv.KeyValue
			else
				begin
				insert into @PayrollIDSelectTable
					select
						PayrollID,
						FullName as PayrollIDName
					from
						dbo.fn_StaffByPayrollID3(@PayrollGroupNumber) tblStaffByPayrollID
				end

			declare @TomorrowStartTime datetime
			declare @tst varchar(20)
			set @tst = dbo.fn_GlobalSettingsGetString('DDReview', 'LaborCalculationTimeSlotStartOfDay')
			if @tst is null
				set @TomorrowStartTime = Convert(datetime,'06:00')
			else
				set @TomorrowStartTime = Convert(datetime,@tst)
			declare @timeslots table (
				DayNumber int,
				TimeSlotNumber int,
				DayTimeSlotNumber int,
				StartTime datetime,
				EndTime datetime
				)
			declare @hourtimeslots table (
				DayNumber int,
				TimeSlotNumber int,
				DayHourSlotNumber int,
				StartTime datetime,
				EndTime datetime
				)
			declare @StartTime datetime
			declare @EndTime datetime
			declare @StartTimeSlot int
			declare @EndTimeSlot int
			declare @NowTimeSlot int
			declare @BaseDate datetime
			declare @SlotShifter int
			declare @MinuteShifter int
			declare @DayNumber int /* */
			set @SlotShifter = floor(datediff(n,'00:00',@TomorrowStartTime)/15)
			set @MinuteShifter = @SlotShifter * 15
			set @BaseDate = dbo.fn_CalcWeekNumberBaseDate()
			set @StartTime = dbo.fn_CalcDayNumberDate(@StartDayNumber)
			set @EndTime = dateadd(n,1439,dbo.fn_CalcDayNumberDate(@EndDayNumber))
			set @StartTimeSlot = datediff(n,@BaseDate,@StartTime)/15+@SlotShifter
			set @EndTimeSlot = datediff(n,@BaseDate,@EndTime)/15+@SlotShifter
			set @NowTimeSlot = @StartTimeSlot
			set @DayNumber = @StartDayNumber
			while @NowTimeSlot<=@EndTimeSlot
				begin
				insert into @timeslots (DayNumber, TimeSlotNumber, DayTimeSlotNumber, StartTime, EndTime)
					values (@DayNumber, @NowTimeSlot, (@NowTimeSlot-@SlotShifter)%96, dateadd(n,@NowTimeSlot*15,@BaseDate), dateadd(n,@NowTimeSlot*15+15,@BaseDate))
				if @NowTimeSlot%4=0
					insert into @hourtimeslots (DayNumber, TimeSlotNumber, DayHourSlotNumber, StartTime, EndTime)
						values (@DayNumber, @NowTimeSlot, floor(((@NowTimeSlot-@SlotShifter)%96)/4), dateadd(n,@NowTimeSlot*15,@BaseDate), dateadd(n,@NowTimeSlot*15+60,@BaseDate))
				if (@NowTimeSlot-@SlotShifter)%96=95
					set @DayNumber = @DayNumber + 1
				set @NowTimeSlot = @NowTimeSlot + 1
				end

			declare @DayHourSlotSelectTable table (
				DayNumber int,
				DayHourSlot int,
				DayHourSlotStart datetime,
				DayHourSlotEnd datetime,
				DayHourSlotName varchar(20)
				)
			if len(@DayHourSlotListCSV)>0
				begin
				insert into @DayHourSlotSelectTable
					select
						hts.DayNumber,
						csv.KeyValue,
						hts.StartTime as DayHourSlotStart,
						hts.EndTime as DayHourSlotEnd,
						substring(convert(varchar(20),hts.StartTime,108),1,5) as DayHourSlotName
					from
						fn_CreateKeyTableValueFromCSVString(@DayHourSlotListCSV) csv
					inner join
						@hourtimeslots hts
					on hts.DayHourSlotNumber=csv.KeyValue
				insert into @DayHourSlotSelectTable
					select distinct
						DayNumber, 2499, null, null, 'Indirect Labor'
					from
						@hourtimeslots hts
					inner join
						fn_CreateKeyTableValueFromCSVString(@DayHourSlotListCSV) csv
					on csv.KeyValue=2499
				end
			else
				begin
				if @DayHourSlotFlag<>0
					begin
					insert into @DayHourSlotSelectTable
						select
							hts.DayNumber,
							csv.KeyValue,
							hts.StartTime as DayHourSlotStart,
							hts.EndTime as DayHourSlotEnd,
							substring(convert(varchar(20),hts.StartTime,108),1,5) as DayHourSlotName
						from
							fn_CreateKeyTableValueFromCSVString('0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23') csv
						inner join
							@hourtimeslots hts
						on hts.DayHourSlotNumber=csv.KeyValue
					insert into @DayHourSlotSelectTable
						select distinct
							DayNumber, 2499, null, null, 'Indirect Labor'
						from @hourtimeslots hts
					end
				else
					begin
					insert into @DayHourSlotSelectTable (DayNumber, DayHourSlot, DayHourSlotStart, DayHourSlotEnd, DayHourSlotName)
						select
							pq.DayNumber,
							0 as KeyValue,
							min(StartTime) as DayHourSlotStart,
							max(EndTime) as DayHourSlotEnd,
							null as DayHourSlotName
						from (
							select DayNumber, TimeSlotNumber, MIN(StartTime) as StartTime, MAX(EndTime) as EndTime from (
								select DayNumber, hts.TimeSlotNumber, hts.StartTime as StartTime, hts.EndTime from @hourtimeslots hts where hts.DayHourSlotNumber=0
								union all
								select DayNumber, hts.TimeSlotNumber-92 as TimeSlotNumber, hts.StartTime as StartTime, hts.EndTime from @hourtimeslots hts where hts.DayHourSlotNumber=23
								) qq
							group by DayNumber, TimeSlotNumber
							) pq
						inner join (
							select distinct	DayNumber from @timeslots
							) rq
						on rq.DayNumber=pq.DayNumber
						group by
							pq.DayNumber
					end
				end

			declare @DayTimeSlotSelectTable table (
				DayNumber int,
				DayTimeSlot int,
				DayTimeSlotStart datetime,
				DayTimeSlotEnd datetime,
				DayTimeSlotName varchar(20)
				)
			if len(@DayTimeSlotListCSV)>0
				begin
				insert into @DayTimeSlotSelectTable
					select
						hts.DayNumber,
						csv.KeyValue,
						hts.StartTime as DayTimeSlotStart,
						hts.EndTime as DayTimeSlotEnd,
						substring(convert(varchar(20),hts.StartTime,108),1,5) as DayTimeSlotName
					from
						fn_CreateKeyTableValueFromCSVString(@DayTimeSlotListCSV) csv
					inner join
						@timeslots hts
					on hts.DayTimeSlotNumber=csv.KeyValue
					insert into @DayTimeSlotSelectTable
						select distinct
							DayNumber, 9996, null, null, 'Indirect Labor'
						from
							@timeslots hts
						inner join
							fn_CreateKeyTableValueFromCSVString(@DayTimeSlotListCSV) csv
						on csv.KeyValue=9996
				end
			else
				begin
				if @DayTimeSlotFlag<>0
					begin
					insert into @DayTimeSlotSelectTable
						select
							hts.DayNumber,
							csv.KeyValue,
							hts.StartTime as DayTimeSlotStart,
							hts.EndTime as DayTimeSlotEnd,
							substring(convert(varchar(20),hts.StartTime,108),1,5) as DayTimeSlotName
						from
							fn_CreateKeyTableValueFromCSVString('0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95') csv
						inner join
							@timeslots hts
						on hts.DayTimeSlotNumber=csv.KeyValue
					insert into @DayTimeSlotSelectTable
						select distinct
							DayNumber, 9996, null, null, 'Indirect Labor'
						from @timeslots hts
					end
				else
					begin
					insert into @DayTimeSlotSelectTable (DayNumber, DayTimeSlot, DayTimeSlotStart, DayTimeSlotEnd, DayTimeSlotName)
						select
							pq.DayNumber,
							0 as KeyValue,
							min(StartTime) as DayTimeSlotStart,
							max(EndTime) as DayTimeSlotEnd,
							null as DayTimeSlotName
						from (
							select DayNumber, TimeSlotNumber, MIN(StartTime) as StartTime, MAX(EndTime) as EndTime from (
								select DayNumber, hts.TimeSlotNumber, hts.StartTime as StartTime, hts.EndTime from @timeslots hts where hts.DayTimeSlotNumber=0
								union all
								select DayNumber, hts.TimeSlotNumber-92 as TimeSlotNumber, hts.StartTime as StartTime, hts.EndTime from @timeslots hts where hts.DayTimeSlotNumber=95
								) qq
							group by DayNumber, TimeSlotNumber
							) pq
						inner join (
							select distinct	DayNumber from @timeslots
							) rq
						on rq.DayNumber=pq.DayNumber
						group by
							pq.DayNumber
					end
				end

			declare @table_variable TABLE (
				RowID int identity (0,1) not null,
				WeekNumber int,
				PayWeekEnding datetime, 
				StoreGroupID int,
				StoreGroupName varchar(60),
				StoreGroupNumber int,
				StoreID int,
				StoreNumber int,
				StoreGCode varchar(50),
				StoreName varchar(4000),
				StaffDepartmentType varchar(50),
				StaffDepartmentTypeID int,
				StaffDepartmentTypeSort varchar(6),
				StaffDepartmentGroup varchar(50),
				StaffDepartmentGroupID int,
				StaffDepartmentGroupSort varchar(6),
				StaffDepartmentName varchar(50),
				StaffDepartmentAbbreviation varchar(10),
				StaffDepartmentID int,
				JobCode int,
				StaffDepartmentSort varchar(6),
				PayrollID bigint,
				PayrollIdentifier varchar(50),
				EmployeeName varchar(50),
				DayHourSlot int,
				DayHourSlotName varchar(20),
				DayTimeSlot int,
				DayTimeSlotName varchar(20),
				StartTime datetime,
				EndTime datetime,
				ShiftDuration money,
				D1 money,
				D2 money,
				D3 money, 
				D4 money,
				D5 money, 
				D6 money,
				D7 money, 
				D1Pay money,
				D2Pay money,
				D3Pay money, 
				D4Pay money,
				D5Pay money, 
				D6Pay money,
				D7Pay money, 
				TotalHours money,
				TotalOTHours money,
				TotalDTHours money,
				TotalOTEligibleHours money,
				Adjustment money,
				HourlyPay money,
				SalaryPay money,
				TotalPay money,
				DeclaredTips money,
				HourlyRate money,
				TotalSales money,
				PercentOfSales decimal(12,4),
				S1 money,
				S2 money,
				S3 money,
				S4 money,
				S5 money,
				S6 money,
				S7 money,
				ST money,
				P1 money,
				P2 money,
				P3 money,
				P4 money,
				P5 money,
				P6 money,
				P7 money,
				PT money
				) 
			declare @ByPayrollID bit
			set @ByPayrollID = case when @PayrollIDFlag=1 then 1 else 0 end
			INSERT INTO @table_variable (WeekNumber, PayWeekEnding, StoreGroupID, StoreGroupName, StoreGroupNumber, StoreID, StoreNumber,
				StoreGCode, StoreName, StaffDepartmentType, StaffDepartmentTypeID, StaffDepartmentTypeSort, StaffDepartmentGroup,
				StaffDepartmentGroupID, StaffDepartmentGroupSort, StaffDepartmentName, StaffDepartmentAbbreviation, StaffDepartmentID,
				JobCode, StaffDepartmentSort, PayrollID, PayrollIdentifier, EmployeeName, DayHourSlot, DayHourSlotName, DayTimeSlot, DayTimeSlotName, StartTime, EndTime, ShiftDuration,
				D1, D2, D3, D4, D5, D6, D7, D1Pay, D2Pay, D3Pay, D4Pay, D5Pay, D6Pay, D7Pay, TotalHours, TotalOTHours, TotalDTHours, TotalOTEligibleHours,
				Adjustment, HourlyPay, SalaryPay, TotalPay, DeclaredTips, HourlyRate, TotalSales, PercentOfSales, S1, S2, S3, S4, S5, S6, S7, ST)
			select
				case when @WeekNumberFlag=0 then null else lcih.PayrollWeekNumber end as WeekNumber,
				case when @WeekNumberFlag=0 and @EmployeeDetailFlag=0 then null else dbo.fn_CalcWeekNumberEndDate(lcih.PayrollWeekNumber,1) end as PayWeekEnding, 
				case when @StoreGroupFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreGroupID end as StoreGroupID,
				case when @StoreGroupFlag=0 and @EmployeeDetailFlag=0 then null else stg.StoreGroupName end as StoreGroupName,
				case when @StoreGroupFlag=0 and @EmployeeDetailFlag=0 then null else stg.StoreGroupNumber end as StoreGroupNumber,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else lcih.StoreID end as StoreID,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreNumber end as StoreNumber,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreGCode end as StoreGCode,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreName end as StoreName,
				case when @TypeFlag=0 and @EmployeeDetailFlag=0 then null else stft.StaffDepartmentType end as StaffDepartmentType,
				case when @TypeFlag=0 and @EmployeeDetailFlag=0 then null else stft.StaffDepartmentTypeID end as StaffDepartmentTypeID,
				min(case when @TypeFlag=0 and @EmployeeDetailFlag=0 then null else stfg.StaffDepartmentTypeID end) as StaffDepartmentTypeSort,
				case when @GroupFlag = 0 and @EmployeeDetailFlag=0 then null else stfg.StaffDepartmentGroup end as StaffDepartmentGroup,
				case when @GroupFlag = 0 and @EmployeeDetailFlag=0 then null else stfg.StaffDepartmentGroupID end as StaffDepartmentGroupID,
				min(case when @GroupFlag = 0 and @EmployeeDetailFlag=0 then null else stfg.StaffDepartmentGroupSort end) as StaffDepartmentGroupSort,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentName end as StaffDepartmentName,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentAbbreviation end as StaffDepartmentAbbreviation,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentID end as StaffDepartmentID,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.JobCode end as JobCode,
				min(case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentSort end) as StaffDepartmentSort,
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null else lcih.PayrollID%@BigNumber end as PayrollID,
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null else stf.PayrollIdentifier end as PayrollIdentifier,
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null
					when @EmployeeFlag=1 then ltrim(rtrim(stf.LastName)) + ', ' + ltrim(rtrim(stf.FirstName))
					when @PayrollIDFlag=1
						then
							cast(
								case when lcih.PayrollID%@BigNumber between 001000001 and 899999999
								then (((lcih.PayrollID%@BigNumber)*@nn)+@nm)%100000000000
								else lcih.PayrollID%@BigNumber
								end as varchar(20)
								)
						else null
					end as EmployeeName,
				case when @DayHourSlotFlag=0 and @DayTimeSlotFlag=0 then 0 else dhsst.DayHourSlot end as DayHourSlot,
				case when @DayHourSlotFlag=0 and @DayTimeSlotFlag=0 then null else dhsst.DayHourSlotName end as DayHourSlotName,
				case when @DayTimeSlotFlag=0 then 0 else lcih.DayTimeSlot end as DayTimeSlot,
				case when @DayTimeSlotFlag=0 then null else dtsst.DayTimeSlotName end as DayTimeSlotName,
				case when @EmployeeDetailFlag = 0 then null else lcih.StartTime end as StartTime,
				case when @EmployeeDetailFlag = 0 then null else dateadd(dd,-datediff(dd,'01/01/1980',lcih.EndTime),lcih.EndTime) end as EndTime,
				case when @EmployeeDetailFlag = 0 then null else lcih.DurationInHours end as ShiftDuration,
				sum(case when PayrollWeekDayNumber = 0 then isnull(lcih.DurationInHours,0) else 0 end) as D1,
				sum(case when PayrollWeekDayNumber = 1 then isnull(lcih.DurationInHours,0) else 0 end) as D2,
				sum(case when PayrollWeekDayNumber = 2 then isnull(lcih.DurationInHours,0) else 0 end) as D3,
				sum(case when PayrollWeekDayNumber = 3 then isnull(lcih.DurationInHours,0) else 0 end) as D4,
				sum(case when PayrollWeekDayNumber = 4 then isnull(lcih.DurationInHours,0) else 0 end) as D5,
				sum(case when PayrollWeekDayNumber = 5 then isnull(lcih.DurationInHours,0) else 0 end) as D6,
				sum(case when PayrollWeekDayNumber = 6 then isnull(lcih.DurationInHours,0) else 0 end) as D7,
				sum(
					case
						when PayrollWeekDayNumber = 0
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D1Pay,
				sum(
					case
						when PayrollWeekDayNumber = 1
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D2Pay,
				sum(
					case
						when PayrollWeekDayNumber = 2
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D3Pay,
				sum(
					case
						when PayrollWeekDayNumber = 3
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D4Pay,
				sum(
					case
						when PayrollWeekDayNumber = 4
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D5Pay,
				sum(
					case
						when PayrollWeekDayNumber = 5
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D6Pay,
				sum(
					case
						when PayrollWeekDayNumber = 6
						then (
							lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
							+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
							+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
							)	* lcih.HourlyRate + SplitShiftAdditionalPay
						else 0
					end
					) as D7Pay,
				sum(lcih.DurationInHours) AS TotalHours,
				sum(@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + lcih.Premium7DayRuleOvertimeInHours) as TotalOTHours,
				sum(@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + lcih.Premium7DayRuleDoubletimeInHours) as TotalDTHours,
				sum(case when lcih.AccruesOvertime=1 then @PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + lcih.Premium7DayRuleOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + lcih.Premium7DayRuleDoubletimeInHours else 0 end) as TotalOTEligibleHours,
				0 as Adjustment,
				sum ((
					lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
					+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours
										+ @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours
										+ @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
					+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours
										+ @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours
										+ Premium7DayRuleDoubletimeInHours)
					)	* lcih.BaseHourlyRate + SplitShiftAdditionalPay)
				as HourlyPay,
				sum(case when lcih.StaffDepartmentID=999 then 0 else 0 end) as SalaryPay,
				sum ((
					lcih.RegularTimeInHours + lcih.SplitShiftAdditionalTimeInHours
					+ 1.5 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumWeeklyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumWeeklyChronologicalOvertimeInHours + @PayrollWeeklyOvertimeProrated*lcih.PremiumDailyOvertimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalOvertimeInHours)
					+ 2.0 * (@PayrollWeeklyOvertimeProrated*lcih.PremiumDailyDoubletimeInHours + @PayrollWeeklyOvertimeChronological*lcih.PremiumDailyChronologicalDoubletimeInHours + Premium7DayRuleDoubletimeInHours)
					)	* lcih.BaseHourlyRate + SplitShiftAdditionalPay) + sum(case when lcih.StaffDepartmentID=999 then 0 else 0 end)
				as TotalPay,
				sum(lcih.POSTipsCash) as DeclaredTips,
				case when @EmployeeDetailFlag = 0 and (@DepartmentFlag=0 or @EmployeeFlag=0)
					then null
					else lcih.BaseHourlyRate
				end as HourlyRate,
				0 as TotalSales,
				0 as PercentOfSales,
				0 as S1,
				0 as S2,
				0 as S3,
				0 as S4,
				0 as S5,
				0 as S6,
				0 as S7,
				0 as ST
			from
				dbo.fn_LaborCalcInHours3" + (ExpandedTimeSlots?"Expanded":"") + @"(@StartDate, @EndDate, 1, @PayrollGroupNumber) lcih
			inner join
				@tblStores sto
			on sto.StoreID=lcih.StoreID
			inner join
				@tblStoreGroups stg
			on stg.StoreGroupID=sto.StoreGroupID
			inner join
				@tblStaffDepartments stfd
			on lcih.StoreID=stfd.StoreID
				and lcih.StaffDepartmentID=stfd.StaffDepartmentID
			inner join
				@tblStaffDepartmentGroups stfg
			on stfd.StoreID=stfg.StoreID
				and stfd.StaffDepartmentGroupID=stfg.StaffDepartmentGroupID
			inner join
				@tblStaffDepartmentTypes stft
			on stft.StoreID=sto.StoreID
				and stft.StaffDepartmentTypeID=stfg.StaffDepartmentTypeID
			inner join
				dbo.fn_Staff3(@PayrollGroupNumber) stf
			on stf.StoreID=lcih.StoreID
				and stf.StoreStaffID=lcih.StoreStaffID
			inner join
				@EmployeeSelectTable est
			on est.KeyValue = lcih.PayrollID
			" + (DayTimeSlotListCSV.Length>0?"inner":"left outer") + @" join
				@DayTimeSlotSelectTable dtsst
			on dtsst.DayTimeSlot=lcih.DayTimeSlot
				and dtsst.DayNumber=lcih.DayNumber
			" + (DayHourSlotListCSV.Length>0?"inner":"left outer") + @" join
				@DayHourSlotSelectTable dhsst
			on dhsst.DayHourSlot=floor(lcih.DayTimeSlot/4)
				and dhsst.DayNumber=lcih.DayNumber
			left outer join
				@PayrollIDSelectTable pid
			on lcih.PayrollID=pid.KeyValue
			group by
				case when @WeekNumberFlag=0 then null else lcih.PayrollWeekNumber end,
				case when @WeekNumberFlag=0 and @EmployeeDetailFlag=0 then null else dbo.fn_CalcWeekNumberEndDate(lcih.PayrollWeekNumber,1) end, 
				case when @StoreGroupFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreGroupID end,
				case when @StoreGroupFlag=0 and @EmployeeDetailFlag=0 then null else stg.StoreGroupName end,
				case when @StoreGroupFlag=0 and @EmployeeDetailFlag=0 then null else stg.StoreGroupNumber end,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else lcih.StoreID end,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreNumber end,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreGCode end,
				case when @StoreFlag=0 and @EmployeeDetailFlag=0 then null else sto.StoreName end,
				case when @TypeFlag=0 and @EmployeeDetailFlag=0 then null else stft.StaffDepartmentType end,
				case when @TypeFlag=0 and @EmployeeDetailFlag=0 then null else stft.StaffDepartmentTypeID end,
				case when @GroupFlag = 0 and @EmployeeDetailFlag=0 then null else stfg.StaffDepartmentGroup end,
				case when @GroupFlag = 0 and @EmployeeDetailFlag=0 then null else stfg.StaffDepartmentGroupID end,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentName end,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentAbbreviation end,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.StaffDepartmentID end,
				case when @DepartmentFlag=0 and @EmployeeDetailFlag=0 then null else stfd.JobCode end,
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null else lcih.PayrollID%@BigNumber end,
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null else stf.PayrollIdentifier end,
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null
					when @EmployeeFlag=1 then ltrim(rtrim(stf.LastName)) + ', ' + ltrim(rtrim(stf.FirstName))
					when @PayrollIDFlag=1
						then
							cast(
								case when lcih.PayrollID%@BigNumber between 001000001 and 899999999
								then (((lcih.PayrollID%@BigNumber)*@nn)+@nm)%100000000000
								else lcih.PayrollID%@BigNumber
								end as varchar(20)
								)
						else null
					end,
				case when @DayHourSlotFlag=0 and @DayTimeSlotFlag=0 then 0 else dhsst.DayHourSlot end,
				case when @DayHourSlotFlag=0 and @DayTimeSlotFlag=0 then null else dhsst.DayHourSlotName end,
				case when @DayTimeSlotFlag=0 then 0 else lcih.DayTimeSlot end,
				case when @DayTimeSlotFlag=0 then null else dtsst.DayTimeSlotName end,
				case when @EmployeeDetailFlag = 0 then null else lcih.StartTime end,
				case when @EmployeeDetailFlag = 0 then null else dateadd(dd,-datediff(dd,'01/01/1980',lcih.EndTime),lcih.EndTime) end,
				case when @EmployeeDetailFlag = 0 then null else lcih.DurationInHours end,
				case when @EmployeeDetailFlag = 0 and (@DepartmentFlag=0 or @EmployeeFlag=0)
					then null
					else lcih.BaseHourlyRate
				end
			order by
				case when @EmployeeFlag=0 and @EmployeeDetailFlag=0 and @PayrollIDFlag=0 then null
					when @EmployeeFlag=1 then ltrim(rtrim(stf.LastName)) + ', ' + ltrim(rtrim(stf.FirstName))
					when @PayrollIDFlag=1
						then
							cast(
								case when lcih.PayrollID%@BigNumber between 001000001 and 899999999
								then (((lcih.PayrollID%@BigNumber)*@nn)+@nm)%100000000000
								else lcih.PayrollID%@BigNumber
								end as varchar(20)
								)
						else null
					end, DayTimeSlot
			if len(@StoreListCSV)=0 -- extracting complete payroll -> adjust overtime rounding for 40 hour reg
					and len(@TypeListCSV)=0
					and len(@GroupListCSV)=0
					and len(@DepartmentListCSV)=0
					and len(@EmployeeListCSV)=0
					and @DepartmentFlag=1
					and @EmployeeFlag=1
				begin
				declare @StoreID int
				declare @PayrollID bigint
				declare @WeekNumber int
				declare @StaffDepartmentID int
				declare @RegularHours money
				declare @OTHours money
				declare cur cursor for
					select PayrollID, WeekNumber, RegularHours, OTHours
					from (
						select PayrollID, WeekNumber, sum(TotalOTEligibleHours-TotalOTHours) as RegularHours, sum(TotalOTHours) as OTHours
						from @table_variable tv
						group by PayrollID, WeekNumber
						) tvs
					where OTHours>0 and RegularHours<>40
				open cur
				fetch next from cur into @PayrollID, @WeekNumber, @RegularHours, @OTHours
				while @@fetch_status=0
					begin
					update @table_variable set adjustment = 40-@RegularHours where PayrollID=@PayrollID and WeekNumber=@WeekNumber
					fetch next from cur into @PayrollID, @WeekNumber, @RegularHours, @OTHours
					end
				close cur
				deallocate cur
				declare @PreviousPayrollID bigint
				declare @PreviousWeekNumber int
				set @PreviousPayrollID = -1
				set @PreviousWeekNumber = -1
				declare cur cursor for
					select PayrollID, WeekNumber, StoreID, StaffDepartmentID from @table_variable where adjustment<>0 order by PayrollID, WeekNumber, TotalHours desc
				open cur
				fetch next from cur into @PayrollID, @WeekNumber, @StoreID, @StaffDepartmentID
				while @@fetch_status=0
					begin
					if @PayrollID<>@PreviousPayrollID or @WeekNumber<>@PreviousWeekNumber
						begin
						update @table_variable
							set Adjustment = 0
							where PayrollID=@PayrollID
								and WeekNumber=@WeekNumber
								and (StoreID<>@StoreID or StaffDepartmentID<>@StaffDepartmentID)
						set @PreviousPayrollID=@PayrollID
						set @PreviousWeekNumber=@WeekNumber
						end
					fetch next from cur into @PayrollID, @WeekNumber, @StoreID, @StaffDepartmentID
					end
				close cur
				deallocate cur
				end
				declare @TotalSales money
				declare @HourSlot money
				declare @TimeSlot money
				declare @Discounts money
				declare @NetSales money
				declare @S1 money
				declare @S2 money
				declare @S3 money
				declare @S4 money
				declare @S5 money
				declare @S6 money
				declare @S7 money
				declare cur cursor for
					select
						case when @WeekNumberFlag=0 then null else floor(css.DayNumber/7) end as WeekNumber,
						case when @StoreFlag<>0 or len(@StoreListCSV)>0 then css.StoreID else null end as StoreID,
						case when @DayHourSlotFlag=0 and @DayTimeSlotFlag=0 then 0 else floor((cssd.TimeInMinutes-@MinuteShifter)/60) end as DayHourSlot,
						case when @DayTimeSlotFlag=0 then 0 else floor((cssd.TimeInMinutes-@MinuteShifter)/15) end as DayTimeSlot,
						sum(case when Discount=0 then cssd.Amount else 0 end) as TotalSales,
						sum(case when Discount<>0 then cssd.Amount else 0 end) as Discounts,
						sum(cssd.Amount) as NetSales,
						sum(case when css.DayNumber%7=0 and Discount=0 then cssd.Amount else 0 end) as S1,
						sum(case when css.DayNumber%7=1 and Discount=0 then cssd.Amount else 0 end) as S2,
						sum(case when css.DayNumber%7=2 and Discount=0 then cssd.Amount else 0 end) as S3,
						sum(case when css.DayNumber%7=3 and Discount=0 then cssd.Amount else 0 end) as S4,
						sum(case when css.DayNumber%7=4 and Discount=0 then cssd.Amount else 0 end) as S5,
						sum(case when css.DayNumber%7=5 and Discount=0 then cssd.Amount else 0 end) as S6,
						sum(case when css.DayNumber%7=6 and Discount=0 then cssd.Amount else 0 end) as S7
					from
						tblCashAndSalesSales css
					inner join
						tblCashAndSalesBatches csb
					on csb.BatchID=css.BatchID
					inner join
						tblSaleTypes st
					on st.StoreID=css.StoreID
						and st.SaleTypeID=css.SaleTypeID
					inner join
						tblCashAndSalesSalesDetail cssd
					on csb.BatchID=cssd.BatchID and css.SaleItemID=cssd.SaleItemID
					inner join
						tblStores sto
					on sto.StoreID=css.StoreID
					where css.DayNumber between @StartDayNumber and @EndDayNumber
						and cssd.DayNumber between @StartDayNumber and @EndDayNumber
						and (@PayrollGroupNumber=8191 and sto.PayrollGroupNumber<>0 or sto.PayrollGroupNumber=@PayrollGroupNumber)
						and st.ExcludeFromSales=0 and css.MainTypeID in (1,2,3)
					group by
						case when @WeekNumberFlag=0 then null else floor(css.DayNumber/7) end,
						case when @StoreFlag<>0 or len(@StoreListCSV)>0 then css.StoreID else null end,
						case when @DayHourSlotFlag=0 and @DayTimeSlotFlag=0 then 0 else floor((cssd.TimeInMinutes-@MinuteShifter)/60) end,
						case when @DayTimeSlotFlag=0 then 0 else floor((cssd.TimeInMinutes-@MinuteShifter)/15) end
				open cur
				fetch next from cur into @WeekNumber, @StoreID, @HourSlot, @TimeSlot, @TotalSales, @Discounts, @NetSales, @S1, @S2, @S3, @S4, @S5, @S6, @S7
				while @@fetch_status=0
					begin
						if @WeekNumber is null
							begin
							if @StoreID is null
								begin
								update @table_variable
									set TotalSales = @TotalSales, S1 = @S1, S2 = @S2, S3 = @S3, S4 = @S4, S5 = @S5, S6 = @S6, S7 = @S7, ST = @TotalSales
								where DayHourSlot=@HourSlot and DayTimeSlot=@TimeSlot
								end
							else
								begin
								update @table_variable
									set TotalSales = @TotalSales, S1 = @S1, S2 = @S2, S3 = @S3, S4 = @S4, S5 = @S5, S6 = @S6, S7 = @S7, ST = @TotalSales
									where (StoreID=@StoreID) and DayHourSlot=@HourSlot and DayTimeSlot=@TimeSlot
								end
							end
						else
							begin
							if @StoreID is null
								begin
								update @table_variable
								set TotalSales = @TotalSales, S1 = @S1, S2 = @S2, S3 = @S3, S4 = @S4, S5 = @S5, S6 = @S6, S7 = @S7, ST = @TotalSales
									where (WeekNumber=@WeekNumber) and DayHourSlot=@HourSlot and DayTimeSlot=@TimeSlot
								end
							else
								begin
								update @table_variable
								set TotalSales = @TotalSales, S1 = @S1, S2 = @S2, S3 = @S3, S4 = @S4, S5 = @S5, S6 = @S6, S7 = @S7, ST = @TotalSales
								where (WeekNumber=@WeekNumber) and (StoreID=@StoreID) and DayHourSlot=@HourSlot and DayTimeSlot=@TimeSlot
								end
							end
					fetch next from cur into @WeekNumber, @StoreID, @HourSlot, @TimeSlot, @TotalSales, @Discounts, @NetSales, @S1, @S2, @S3, @S4, @S5, @S6, @S7
					end
				close cur
				deallocate cur
				if @GroupFlag=0 and len(@GroupListCSV)=0
					and @DepartmentFlag=0 and len(@DepartmentListCSV)=0
					and @EmployeeFlag=0 and len(@EmployeeListCSV)=0
					and @EmployeeDetailFlag=0
					begin
					declare cur cursor for 
						select
							case when @WeekNumberFlag=0 then null else qryBudgetMain.WeekNumber end as WeekNumber,
							case when @StoreFlag=0 then null else qryBudgetMain.StoreID end as StoreID,
							case when @TypeFlag=0 then null else qryBudgetMain.StaffDepartmentTypeID end as StaffDepartmentTypeID,
							sum(qryBudgetMain.LaborOverhead) as LaborOverHead
						from (
							select floor(dbo.fn_CalcDayNumber(tblBudgetMain.CurrentYearDate)/7) as WeekNumber,
								tblBudgetMain.StoreID as StoreID,
								2 as StaffDepartmentTypeID,
								FOHLaborOverHead as LaborOverHead
							from
								tblBudgetMain
							inner join 
								@tblStores tblStores
							on tblStores.StoreID=tblBudgetMain.StoreID
							inner join
								@tblStaffDepartmentTypes sdt
							on sdt.StoreID=tblBudgetMain.StoreID
								and sdt.StaffDepartmentTypeID=2
							where dbo.fn_CalcDayNumber(tblBudgetMain.CurrentYearDate) between @StartDayNumber and @EndDayNumber
							union all
							select floor(dbo.fn_CalcDayNumber(tblBudgetMain.CurrentYearDate)/7) as WeekNumber,
								tblBudgetMain.StoreID as StoreID,
								3 as StaffDepartmentTypeID,
								BOHLaborOverHead as LaborOverHead
							from
								tblBudgetMain
							inner join 
								@tblStores tblStores
							on tblStores.StoreID=tblBudgetMain.StoreID
							inner join
								@tblStaffDepartmentTypes sdt
							on sdt.StoreID=tblBudgetMain.StoreID
								and sdt.StaffDepartmentTypeID=3
							where dbo.fn_CalcDayNumber(tblBudgetMain.CurrentYearDate) between @StartDayNumber and @EndDayNumber
							union all
							select floor(dbo.fn_CalcDayNumber(tblBudgetMain.CurrentYearDate)/7) as WeekNumber,
								tblBudgetMain.StoreID as StoreID,
								4 as StaffDepartmentTypeID,
								ManagementLaborOverHead as LaborOverHead
							from
								tblBudgetMain
							inner join 
								@tblStores tblStores
							on tblStores.StoreID=tblBudgetMain.StoreID
							inner join
								@tblStaffDepartmentTypes sdt
							on sdt.StoreID=tblBudgetMain.StoreID
								and sdt.StaffDepartmentTypeID=4
							where dbo.fn_CalcDayNumber(tblBudgetMain.CurrentYearDate) between @StartDayNumber and @EndDayNumber
							) qryBudgetMain
						where
							qryBudgetMain.LaborOverHead is not null
						group by
							case when @WeekNumberFlag=0 then null else qryBudgetMain.WeekNumber end,
							case when @StoreFlag=0 then null else qryBudgetMain.StoreID end,
							case when @TypeFlag=0 then null else qryBudgetMain.StaffDepartmentTypeID end
					declare @StaffDepartmentTypeID int
					declare @LaborOverHead money
					open cur
					fetch next from cur into @WeekNumber, @StoreID, @StaffDepartmentTypeID, @LaborOverHead
					while @@fetch_status=0
						begin
						update @table_variable
						set SalaryPay = @LaborOverHead, TotalPay = isnull(HourlyPay,0) + isnull(@LaborOverHead,0)
						where (@WeekNumber is null or WeekNumber=@WeekNumber)
							and (@StoreID is null or StoreID=@StoreID)
							and (@StaffDepartmentTypeID is null or StaffDepartmentTypeID=@StaffDepartmentTypeID)
						fetch next from cur into @WeekNumber, @StoreID, @StaffDepartmentTypeID, @LaborOverHead
						end
					close cur
					deallocate cur
					end
			update @table_variable
				set PayrollID=((PayrollID%@BigNumber)*@nn+@nm)%100000000000,
				PayrollIdentifier = cast(((PayrollID%@BigNumber)*@nn+@nm)%100000000000 as varchar(20))
			where PayrollID%@BigNumber between 001000001 and 899999999
			update @table_variable
				set PercentOfSales = case when TotalSales=0 then 0 else floor(10000*TotalPay/TotalSales+0.5)/100 end,
					P1 = case when S1<>0 then floor(D1Pay/S1*100+0.5)/100 else 0 end,
					P2 = case when S2<>0 then floor(D2Pay/S2*100+0.5)/100 else 0 end,
					P3 = case when S3<>0 then floor(D3Pay/S3*100+0.5)/100 else 0 end,
					P4 = case when S4<>0 then floor(D4Pay/S4*100+0.5)/100 else 0 end,
					P5 = case when S5<>0 then floor(D5Pay/S5*100+0.5)/100 else 0 end,
					P6 = case when S6<>0 then floor(D6Pay/S6*100+0.5)/100 else 0 end,
					P7 = case when S7<>0 then floor(D7Pay/S7*100+0.5)/100 else 0 end,
					PT = case when ST<>0 then floor(HourlyPay/ST*100+0.5)/100 else 0 end
			select * from @table_variable tv
			");
			return (sb.ToString());
			}

		public String BuildHeaderScript() {
			StringBuilder sb = new StringBuilder();
			sb.Append(@"declare @StartDayNumber int
			declare @EndDayNumber int
			declare @StoreGroupFlag bit
			declare @StoreGroupListCSV varchar(8000)
			declare @StoreFlag bit
			declare @StoreListCSV varchar(8000)
			declare @TypeFlag bit
			declare @TypeListCSV varchar(8000)
			declare @GroupFlag bit
			declare @GroupListCSV varchar(8000)
			declare @DepartmentFlag bit
			declare @DepartmentListCSV varchar(8000)
			declare @EmployeeFlag bit
			declare @EmployeeListCSV varchar(8000)
			declare @PayrollIDFlag bit
			declare @PayrollIDListCSV varchar(8000)
			declare @DayHourSlotFlag bit
			declare @DayHourSlotListCSV varchar(8000)
			declare @DayTimeSlotFlag bit
			declare @DayTimeSlotListCSV varchar(8000)
			declare @EmployeeDetailFlag bit
			");
			sb.Append(
				@"set @StartDayNumber=" + StartDayNumber.ToString() + "\n"
				+ @"set @EndDayNumber=" + StartDayNumber.ToString() + "\n"
				+ @"set @StoreGroupFlag=" + StoreGroupFlag.ToString() + "\n"
				+ @"set @StoreGroupListCSV='" + StoreGroupListCSV.ToString() + "'\n"
				+ @"set @StoreFlag=" + StoreFlag.ToString() + "\n"
				+ @"set @StoreListCSV='" + StoreListCSV.ToString() + "'\n"
				+ @"set @TypeFlag=" + LaborTypeFlag.ToString() + "\n"
				+ @"set @TypeListCSV='" + LaborTypeListCSV.ToString() + "'\n"
				+ @"set @GroupFlag=" + LaborGroupFlag.ToString() + "\n"
				+ @"set @GroupListCSV='" + LaborGroupListCSV.ToString() + "'\n"
				+ @"set @DepartmentFlag=" + LaborDepartmentFlag.ToString() + "\n"
				+ @"set @DepartmentListCSV='" + LaborDepartmentListCSV.ToString() + "'\n"
				+ @"set @EmployeeFlag=" + EmployeeFlag.ToString() + "\n"
				+ @"set @EmployeeListCSV='" + EmployeeListCSV.ToString() + "'\n"
				+ @"set @PayrollIDFlag=" + PayrollIDFlag.ToString() + "\n"
				+ @"set @PayrollIDListCSV='" + PayrollIDListCSV.ToString() + "'\n"
				+ @"set @DayHourSlotFlag=" + DayHourSlotFlag.ToString() + "\n"
				+ @"set @DayHourSlotListCSV='" + DayHourSlotListCSV.ToString() + "'\n"
				+ @"set @DayTimeSlotFlag=" + DayTimeSlotFlag.ToString() + "\n"
				+ @"set @DayTimeSlotListCSV='" + DayTimeSlotListCSV.ToString() + "'\n"
				+ @"set @EmployeeDetailFlag=" + EmployeeDetailFlag.ToString() + "\n");
			sb.Append(@"

			declare @PayrollIDLowerLimit bigint
			declare @PayrollIDUpperLimit bigint
			declare @PayrollGroupNumber bigint
			set @PayrollGroupNumber = 8191
			set @PayrollIDLowerLimit = (select PayrollIDLowerLimit from tblGlobal)
			set @PayrollIDUpperLimit = (select PayrollIDUpperLimit from tblGlobal)+1

			declare @StoreGroupSelectTable table(KeyValue int, StoreGroupName varchar(50), StoreGroupNumber int)
			declare @StoreSelectTable table(KeyValue int, StoreName varchar(50), StoreGCode varchar(50), StoreNumber int)
			declare @TypeSelectTable table(KeyValue int, StoreID int, StaffDepartmentType varchar(50), StaffDepartmentTypeSort varchar(6))
			declare @GroupSelectTable table(KeyValue int, StoreID int, StaffDepartmentGroup varchar(50), StaffDepartmentGroupSort varchar(6))
			declare @DepartmentSelectTable table (KeyValue int, StoreID int, StaffDepartmentName varchar(50), StaffDepartmentSort varchar(6))
			declare @EmployeeSelectTable table (KeyValue bigint, EmployeeName varchar(255))
			declare @PayrollIDSelectTable table (KeyValue bigint, PayrollIDName varchar(102))
			declare @TotalCount int

			declare @StoreGroupMultipleFlag bit
			declare @StoreMultipleFlag bit
			declare @TypeMultipleFlag bit
			declare @GroupMultipleFlag bit
			declare @DepartmentMultipleFlag bit
			declare @EmployeeMultipleFlag bit
			declare @PayrollIDMultipleFlag bit
			declare @DayHourSlotMultipleFlag bit
			declare @DayTimeSlotMultipleFlag bit

			declare @StoreGroupDetailsFlag bit
			declare @StoreDetailsFlag bit
			declare @TypeDetailsFlag bit
			declare @GroupDetailsFlag bit
			declare @DepartmentDetailsFlag bit
			declare @EmployeeDetailsFlag bit
			declare @PayrollIDDetailsFlag bit
			declare @DayHourSlotDetailsFlag bit
			declare @DayTimeSlotDetailsFlag bit

			set @StoreGroupMultipleFlag = 0
			set @StoreMultipleFlag = 0
			set @TypeMultipleFlag = 0
			set @GroupMultipleFlag = 0
			set @DepartmentMultipleFlag = 0
			set @EmployeeMultipleFlag = 0
			set @PayrollIDMultipleFlag = 0

			set @StoreGroupDetailsFlag = 0
			set @StoreDetailsFlag = 0
			set @TypeDetailsFlag = 0
			set @GroupDetailsFlag = 0
			set @DepartmentDetailsFlag = 0
			set @EmployeeDetailsFlag = 0
			set @PayrollIDDetailsFlag = 0

			if len(@StoreGroupListCSV)>0
				begin
				insert into @StoreGroupSelectTable
				select KeyValue, tblStoreGroups.StoreGroupName, tblStoreGroups.StoreGroupNumber
				from fn_CreateKeyTableValueFromCSVString(@StoreGroupListCSV) csv inner join tblStoreGroups on csv.KeyValue=tblStoreGroups.StoreGroupID
				set @TotalCount = (select count(*) as sgCount from @StoreGroupSelectTable sg)
				set @StoreGroupMultipleFlag = case when (@TotalCount>1) then 1 else 0 end
				set @StoreGroupDetailsFlag = case when (@TotalCount>8) then 1 else 0 end
				end
			else 
				insert into @StoreGroupSelectTable(KeyValue, StoreGroupName, StoreGroupNumber)
				select distinct StoreGroupID as KeyValue, StoreGroupName, StoreGroupNumber from tblStoreGroups

			declare @StoreGroupNameList varchar(1000)
			set @StoreGroupNameList = null
			declare @StoreGroupID int
			declare @StoreGroupName varchar(50)
			declare @StoreGroupNumber int
			declare cur cursor for select distinct KeyValue, StoreGroupName, StoreGroupNumber from @StoreGroupSelectTable order by StoreGroupNumber
			open cur
			fetch next from cur into @StoreGroupID, @StoreGroupName, @StoreGroupNumber
			while @@fetch_status=0
				begin
				if @StoreGroupNameList is null
					set @StoreGroupNameList = ''
				else
					if  len(@StoreGroupNameList)<995
						set @StoreGroupNameList = @StoreGroupNameList + '<br/>'
				set @StoreGroupNameList = left(@StoreGroupNameList,1000)
				fetch next from cur into @StoreGroupID, @StoreGroupName, @StoreGroupNumber
				end
			close cur
			deallocate cur

			if len(@StoreListCSV)>0
				begin
				insert into @StoreSelectTable
				select KeyValue, isnull(tblStores.StoreReportName,tblStores.StoreName) as StoreName, tblStores.StoreGCode, tblStores.StoreNumber
				from fn_CreateKeyTableValueFromCSVString(@StoreListCSV) csv inner join tblStores on csv.KeyValue=tblStores.StoreID
				set @TotalCount = (select count(*) as sgCount from @StoreSelectTable sg)
				set @StoreMultipleFlag = case when (@TotalCount>1) then 1 else 0 end
				set @StoreDetailsFlag = case when (@TotalCount>8) then 1 else 0 end
				end
			else 
				insert into @StoreSelectTable(KeyValue, StoreName, StoreGCode, StoreNumber)
				select distinct StoreID as KeyValue, isnull(tblStores.StoreReportName,tblStores.StoreName) as StoreName, StoreGCode, StoreNumber from tblStores where Active=1

			declare @StoreNameList varchar(1000)
			set @StoreNameList = null
			declare @StoreID int
			declare @StoreName varchar(50)
			declare @StoreNumber int
			declare @StoreGCode varchar(50)
			declare cur cursor for select distinct KeyValue, StoreName, StoreGCode, StoreNumber from @StoreSelectTable order by StoreNumber
			open cur
			fetch next from cur into @StoreID, @StoreName, @StoreGCode, @StoreNumber
			while @@fetch_status=0
				begin
				if @StoreNameList is null
					set @StoreNameList = ''
				else
					if  len(@StoreNameList)<995
						set @StoreNameList = @StoreNameList + '<br/>'
				set @StoreNameList = left(@StoreNameList + rtrim(isnull(@StoreName,''))+isnull(' ('+rtrim(@StoreGCode)+')',''),1000)
				fetch next from cur into @StoreID, @StoreName, @StoreGCode, @StoreNumber
				end
			close cur
			deallocate cur

			declare @tblStaffDepartmentTypes table(StoreID int, StaffDepartmentTypeID int, StaffDepartmentType varchar(50), StaffDepartmentTypeSort varchar(6))
			insert into @tblStaffDepartmentTypes
				select distinct
					StoreID,
					StaffDepartmentTypeID,
					StaffDepartmentType,
					min(StaffDepartmentTypeSort) as StaffDepartmentTypeSort
				from
					tblStaffDepartmentGroups sdg
				inner join
					@StoreSelectTable sst
				on sdg.StoreID=sst.KeyValue
				group by
					StoreID, 
					StaffDepartmentTypeID,
					StaffDepartmentType

			if len(@TypeListCSV)>0
				insert into @TypeSelectTable
				select
					csv.KeyValue,
					tblStaffDepartmentTypes.StoreID,
					tblStaffDepartmentTypes.StaffDepartmentType,
					min(tblStaffDepartmentTypes.StaffDepartmentTypeSort) as StaffDepartmentTypeSort
				from fn_CreateKeyTableValueFromCSVString(@TypeListCSV) csv
				inner join
					@tblStaffDepartmentTypes tblStaffDepartmentTypes
				on csv.KeyValue=tblStaffDepartmentTypes.StaffDepartmentTypeID
				inner join
					@StoreSelectTable sst
				on tblStaffDepartmentTypes.StoreID=sst.KeyValue
				group by
					csv.KeyValue,
					tblStaffDepartmentTypes.StaffDepartmentType,
					tblStaffDepartmentTypes.StoreID
			else 
				insert into @TypeSelectTable(KeyValue, StoreID, StaffDepartmentType, StaffDepartmentTypeSort)
				select distinct
					tblStaffDepartmentTypes.StaffDepartmentTypeID as KeyValue, tblStaffDepartmentTypes.StoreID, tblStaffDepartmentTypes.StaffDepartmentType, tblStaffDepartmentTypes.StaffDepartmentTypeSort
				from
					@tblStaffDepartmentTypes tblStaffDepartmentTypes
				inner join
					@StoreSelectTable sst
				on tblStaffDepartmentTypes.StoreID=sst.KeyValue

			declare @TypeNameList varchar(1000)
			set @TypeNameList = null
			if @TypeFlag=0
				begin
				declare @StaffDepartmentID int
				declare @StaffDepartmentType varchar(50)
				declare @StaffDepartmentTypeSort varchar(6)
				declare cur cursor for
					select distinct
						tst.KeyValue, 
						tst.StaffDepartmentType,
						StaffDepartmentTypeSort
					from
						@TypeSelectTable tst
					inner join
						@StoreSelectTable sst
					on tst.StoreID=sst.KeyValue
					order by
						StaffDepartmentTypeSort
				open cur
				fetch next from cur into @StaffDepartmentID, @StaffDepartmentType, @StaffDepartmentTypeSort
				while @@fetch_status=0
					begin
					if @TypeNameList is null
						set @TypeNameList = ''
					else
						if  len(@TypeNameList)<995
							set @TypeNameList = @TypeNameList + '<br/>'
					set @TypeNameList = left(@TypeNameList + rtrim(isnull(@StaffDepartmentType,'')),1000)
					fetch next from cur into @StaffDepartmentID, @StaffDepartmentType, @StaffDepartmentTypeSort
					end
				close cur
				deallocate cur
				end

			if len(@GroupListCSV)>0
				insert into @GroupSelectTable
				select
					csv.KeyValue, tblStaffDepartmentGroups.StoreID, tblStaffDepartmentGroups.StaffDepartmentGroup, min(tblStaffDepartmentGroups.StaffDepartmentGroupSort) as StaffDepartmentGroupSort
				from fn_CreateKeyTableValueFromCSVString(@GroupListCSV) csv
				inner join
					tblStaffDepartmentGroups
				on csv.KeyValue=tblStaffDepartmentGroups.StaffDepartmentGroupID
				inner join
					@TypeSelectTable tst
				on tblStaffDepartmentGroups.StaffDepartmentTypeID=tst.KeyValue");
				if (LaborGroupListCSV.Length>0)
					sb.Append ("\n\t\t\t\twhere (" + ParsePairSetOutput(LaborGroupListCSV,"tblStaffDepartmentGroups.StoreID","tblStaffDepartmentGroups.StaffDepartmentGroupID") + ")");
				sb.Append(@"
				group by
					csv.KeyValue,
					tblStaffDepartmentGroups.StoreID,
					tblStaffDepartmentGroups.StaffDepartmentGroup
			else 
				insert into @GroupSelectTable(KeyValue, StoreID, StaffDepartmentGroup, StaffDepartmentGroupSort)
				select distinct
					tblStaffDepartmentGroups.StaffDepartmentGroupID as KeyValue, tblStaffDepartmentGroups.StoreID, tblStaffDepartmentGroups.StaffDepartmentGroup, tblStaffDepartmentGroups.StaffDepartmentGroupSort
				from
					tblStaffDepartmentGroups
				inner join
					@TypeSelectTable tst
				on tblStaffDepartmentGroups.StaffDepartmentTypeID=tst.KeyValue

			declare @GroupNameList varchar(1000)
			set @GroupNameList = null
			if @GroupFlag=0
				begin
				declare @StaffDepartmentGroup varchar(50)
				declare @StaffDepartmentGroupSort varchar(6)
				declare cur cursor for
					select distinct
						gst.KeyValue,
						gst.StaffDepartmentGroup,
						min(gst.StaffDepartmentGroupSort) as StaffDepartmentGroupSort
					from
						@GroupSelectTable gst
					inner join
						@StoreSelectTable sst
					on gst.StoreID=sst.KeyValue
					group by gst.KeyValue, gst.StaffDepartmentGroup
					order by min(gst.StaffDepartmentGroupSort)
				open cur
				fetch next from cur into @StaffDepartmentID, @StaffDepartmentGroup, @StaffDepartmentGroupSort
				while @@fetch_status=0
					begin
					if @GroupNameList is null
						set @GroupNameList = ''
					else
						if  len(@GroupNameList)<995
							set @GroupNameList = @GroupNameList + '<br/>'
					set @GroupNameList = left(@GroupNameList + rtrim(isnull(@StaffDepartmentGroup,'')),1000)
					fetch next from cur into @StaffDepartmentID, @StaffDepartmentGroup, @StaffDepartmentGroupSort
					end
				close cur
				deallocate cur
				end

			if len(@DepartmentListCSV)>0
				insert into @DepartmentSelectTable
				select
					csv.KeyValue, tblStaffDepartments.StoreID, tblStaffDepartments.StaffDepartmentName, min(tblStaffDepartments.StaffDepartmentSort) as StaffDepartmentSort
				from fn_CreateKeyTableValueFromCSVString(@DepartmentListCSV) csv
				inner join
					dbo.fn_StaffDepartments() tblStaffDepartments
				on csv.KeyValue=tblStaffDepartments.StaffDepartmentID
				inner join
					@GroupSelectTable gst
				on tblStaffDepartments.StaffDepartmentGroupID=gst.KeyValue");
				if (LaborDepartmentListCSV.Length>0)
					sb.Append ("\n\t\t\t\twhere (" + ParsePairSetOutput(LaborDepartmentListCSV,"tblStaffDepartments.StoreID","tblStaffDepartments.StaffDepartmentID") + ")");
				sb.Append (@"
				group by
					csv.KeyValue,
					tblStaffDepartments.StoreID,
					tblStaffDepartments.StaffDepartmentName
			else 
				insert into @DepartmentSelectTable(KeyValue, StoreID, StaffDepartmentName, StaffDepartmentSort)
				select distinct
					tblStaffDepartments.StaffDepartmentID as KeyValue, tblStaffDepartments.StoreID, tblStaffDepartments.StaffDepartmentName, tblStaffDepartments.StaffDepartmentSort
				from
					dbo.fn_StaffDepartments() tblStaffDepartments
				inner join
					@GroupSelectTable gst
				on tblStaffDepartments.StaffDepartmentGroupID=gst.KeyValue

			declare @DepartmentNameList varchar(1000)
			set @DepartmentNameList = null
			if @DepartmentFlag=0
				if len(@DepartmentListCSV)=0
					set @DepartmentNameList = 'All'
				else
					begin
					declare @StaffDepartmentName varchar(50)
					declare @StaffDepartmentSort varchar(6)
					declare cur cursor for
						select distinct
							dst.KeyValue,
							dst.StaffDepartmentName,
							min(dst.StaffDepartmentSort) as StaffDepartmentSort
						from
							@DepartmentSelectTable dst
						inner join
							@StoreSelectTable sst
						on dst.StoreID=sst.KeyValue
						group by dst.KeyValue, dst.StaffDepartmentName
						order by min(dst.StaffDepartmentSort)
					open cur
					fetch next from cur into @StaffDepartmentID, @StaffDepartmentName, @StaffDepartmentSort
					while @@fetch_status=0
						begin
						if @DepartmentNameList is null
							set @DepartmentNameList = ''
						else
							if  len(@DepartmentNameList)<995
								set @DepartmentNameList = @DepartmentNameList + '<br/>'
						set @DepartmentNameList = left(@DepartmentNameList + rtrim(isnull(@StaffDepartmentName,'')),1000)
						fetch next from cur into @StaffDepartmentID, @StaffDepartmentName, @StaffDepartmentSort
						end
					close cur
					deallocate cur
					end

			if len(@EmployeeListCSV)>0
				insert into @EmployeeSelectTable
					select
						csv.KeyValue,
						qem.EmployeeName
					from
						fn_CreateBigIntKeyTableValueFromCSVString(@EmployeeListCSV) csv
					inner join (
						select
							case when tblStaff.PayrollID=0 then tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end as PayrollID,
							max(isnull(rtrim(tblStaff.LastName)+', ','')+isnull(rtrim(tblStaff.FirstName),'')) as EmployeeName
						from
							tblStaff
						inner join
							@StoreSelectTable sst
						on tblStaff.StoreID=sst.KeyValue
						group by case when tblStaff.PayrollID=0 then tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end
						) qem
					on qem.PayrollID=csv.KeyValue
			else
				begin
				insert into @EmployeeSelectTable
					select
						case when tblStaff.PayrollID=0 then tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end as KeyValue,
						max(isnull(rtrim(tblStaff.LastName)+', ','')+isnull(rtrim(tblStaff.FirstName),'')) as EmployeeName
					from
						tblStaff
					inner join
						@StoreSelectTable sst
					on tblStaff.StoreID=sst.KeyValue
					group by case when tblStaff.PayrollID=0 then tblStaff.StoreID*100000 + tblStaff.StoreStaffID else tblStaff.PayrollID end
				insert into @EmployeeSelectTable (KeyValue, EmployeeName) values (99000, 'Management Overhead')
				end

			declare @EmployeeNameList varchar(2000)
			set @EmployeeNameList = null
			if @EmployeeFlag=0
				begin
				if len(@EmployeeListCSV)=0
					set @EmployeeNameList = 'All'
				else
					begin
					if (select count(KeyValue) from @EmployeeSelectTable)>10
						set @EmployeeNameList = 'Too Many To List'
					else
						begin
						declare @PayrollID bigint
						declare @EmployeeName varchar(50)
						declare cur cursor for
							select distinct
								KeyValue,
								min(EmployeeName) as EmployeeName
							from @EmployeeSelectTable
							group by KeyValue
							order by min(EmployeeName)
						open cur
						fetch next from cur into @PayrollID, @EmployeeName
						while @@fetch_status=0
							begin
							if @EmployeeNameList is null
								set @EmployeeNameList = ''
							else
								if len(@EmployeeNameList)<1995
									set @EmployeeNameList = @EmployeeNameList + '<br/>'
							set @EmployeeNameList = left(@EmployeeNameList + rtrim(isnull(@EmployeeName,'')),2000)
							fetch next from cur into @PayrollID, @EmployeeName
							end
						close cur
						deallocate cur
						end
					end
				end

			if len(@PayrollIDListCSV)>0
				insert into @PayrollIDSelectTable
					select
						csv.KeyValue,
						qem.PayrollIDName
					from
						fn_CreateBigIntKeyTableValueFromCSVString(@PayrollIDListCSV) csv
					inner join (
						select
							PayrollID,
							FullName as PayrollIDName
						from
							dbo.fn_StaffByPayrollID3(@PayrollGroupNumber) tblStaff
						) qem
					on qem.PayrollID=csv.KeyValue
			else
				begin
				insert into @PayrollIDSelectTable
					select
						PayrollID,
						FullName as PayrollIDName
					from
						dbo.fn_StaffByPayrollID3(@PayrollGroupNumber) tblStaffByPayrollID
				end

			declare @PayrollIDNameList varchar(2000)
			set @PayrollIDNameList = null
			if @PayrollIDFlag=0
				begin
				if len(@PayrollIDListCSV)=0
					set @PayrollIDNameList = 'All'
				else
					begin
					if (select count(KeyValue) from @PayrollIDSelectTable)>10
						set @PayrollIDNameList = 'Too Many To List'
					else
						begin
						declare @PayrollIDName varchar(50)
						declare cur cursor for
							select distinct
								KeyValue,
								min(PayrollIDName) as PayrollIDName
							from @PayrollIDSelectTable
							group by KeyValue
							order by min(PayrollIDName)
						open cur
						fetch next from cur into @PayrollID, @PayrollIDName
						while @@fetch_status=0
							begin
							if @PayrollIDNameList is null
								set @PayrollIDNameList = ''
							else
								if len(@PayrollIDNameList)<1995
									set @PayrollIDNameList = @PayrollIDNameList + '<br/>'
							set @PayrollIDNameList = left(@PayrollIDNameList + rtrim(isnull(@PayrollIDName,'')),2000)
							fetch next from cur into @PayrollID, @PayrollIDName
							end
						close cur
						deallocate cur
						end
					end
				end
			declare @table_variable TABLE (
				StoreGroupName varchar(900),
				StoreName varchar(1000),
				StaffDepartmentType varchar(900),
				StaffDepartmentGroup varchar(600),
				StaffDepartmentName varchar(890),
				PayrollID varchar(800),
				EmployeeName varchar(1300),
				PayrollIDName varchar(1300),
				DayHourSlotName varchar(500),
				DayTimeSlotName varchar(500),
				StoreGroupDetailFlag bit,
				StoreDetailFlag bit,
				LaborTypeDetailFlag bit,
				LaborGroupDetailFlag bit,
				LaborDepartmentDetailFlag bit,
				EmployeeDetailFlag bit,
				PayrollIDDetailFlag bit,
				DayHourSlotDetailFlag bit,
				DayTimeSlotDetailFlag bit,
				StoreGroupMultipleFlag bit,
				StoreMultipleFlag bit,
				LaborTypeMultipleFlag bit,
				LaborGroupMultipleFlag bit,
				LaborDepartmentMultipleFlag bit,
				EmployeeMultipleFlag bit,
				PayrollIDMultipleFlag bit,
				DayHourSlotMultipleFlag bit,
				DayTimeSlotMultipleFlag bit
				) 
			INSERT INTO @table_variable (StoreGroupName, StoreName, StaffDepartmentType, StaffDepartmentGroup, StaffDepartmentName, EmployeeName, PayrollIDName, DayHourSlotName, DayTimeSlotName,
					StoreGroupDetailFlag, StoreDetailFlag, LaborTypeDetailFlag, LaborGroupDetailFlag, LaborDepartmentDetailFlag, EmployeeDetailFlag, PayrollIDDetailFlag, DayHourSlotDetailFlag, DayTimeSlotDetailFlag,
					StoreGroupMultipleFlag, StoreMultipleFlag, LaborTypeMultipleFlag, LaborGroupMultipleFlag, LaborDepartmentMultipleFlag, EmployeeMultipleFlag, PayrollIDMultipleFlag, DayHourSlotMultipleFlag, DayTimeSlotMultipleFlag) 
				values (@StoreGroupNameList, @StoreNameList, @TypeNameList, @GroupNameList, @DepartmentNameList, @EmployeeNameList, @PayrollIDNameList, '', '', 
					@StoreGroupDetailsFlag, @StoreDetailsFlag, 0, 0, 0, 0, 0, 0, 0, 
					@StoreGroupMultipleFlag, @StoreMultipleFlag, 0, 0, 0, 0, 0, 0, 0)
			select * from @table_variable table_variable
			");
			return (sb.ToString());
			}
		}
	}
