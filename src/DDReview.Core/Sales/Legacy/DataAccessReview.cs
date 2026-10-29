#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO;

namespace DDReview.Core.Sales.Legacy {
	public class DataAccessReview {
		public class SelectParameterList : List<SelectParameterList.Parameter> {
			public static String SingleQuoteString(String nn) {
				return "'" + nn + "'";
				}
			public override String ToString() {
				StringBuilder sb = new StringBuilder();
				foreach (SelectParameterList.Parameter sp in this) {
					sb.Append(sp.ToString(0));
					}
				foreach (SelectParameterList.Parameter sp in this) {
					sb.Append(sp.ToString(1));
					}
				return sb.ToString();
				}
			public class Parameter {
				public String ParameterName;
				public String ParameterType;
				public String ParameterExpression;
				public String ToString(Int32 LineNumber) {
					if (LineNumber==0)
						return "\t\t\tdeclare @" + ParameterName + " " + ParameterType + "\n";
					else
						return "\t\t\tset @" + ParameterName + " = " + ParameterExpression + "\n";
					}
				public Parameter(String ParameterName, String ParameterType, String ParameterExpression) {
					this.ParameterName = ParameterName; this.ParameterType=ParameterType; this.ParameterExpression = ParameterExpression;
					}
				public Parameter() {}
				}
			}
		public class EmptySelect : Select {
			public override String OutputSelect(Int32 TabCount) { return ""; }
			}
		public abstract class Select {
			public SelectParameterList SelectParameters = new SelectParameterList();
			public virtual String OutputSelect() {
				return this.OutputSelect(TabCount);
				}
			public Boolean OutputDetailFields = false;
			public abstract String OutputSelect(Int32 TabCount);
			public Dictionary<String,Dimension> Dimensions = new Dictionary<String,Dimension>();
			public Int32 TabCount = 5;
			public List<String> JoinExpressions = new List<String>();
			public Boolean WhereFlag = true; /* set to false when first where expression has been output */
			public Boolean Condition {
				get {
					Boolean cn = false;
					foreach (KeyValuePair<String,Dimension> p in Dimensions) {
						Dimension d = p.Value;
						if (d.Condition) {
							cn = true;
							break;
							}
						}
					return cn;
					}
				}
			public enum CommaModes { Before, After, BeforeAndAfter, None }
			public enum AndModes { Before, None }
			public String Repeat(String input, Int32 count) {
				StringBuilder builder = new StringBuilder((input == null ? 0 : input.Length) * count);
				for(int i = 0; i < count; i++) builder.Append(input);
				return builder.ToString();
				}
			public String TabString(Int32 TabCount) {
				return "\n" + Repeat("\t",TabCount);
				}
			public String FixTabs(String InputTabString) {
				Int32 MinTabCount = Int32.MaxValue;
				Int32 TabCount=0;
				Boolean Started = false;
				for (Int32 ii=0;ii<InputTabString.Length;ii++) {
					if (InputTabString[ii]=='\n') {
						if (TabCount<MinTabCount && Started)
							MinTabCount = TabCount;
						TabCount = 0;
						}
					else if (InputTabString[ii]=='\t') {
						TabCount++;
						Started = true;
						}
					}
				return InputTabString.Replace(TabString(MinTabCount),"\n\t\t\t\t\t");
				}
			public String ParsePairSetOutput(String strInput, String str1, String str2) {
				System.Text.StringBuilder sbWhere = new System.Text.StringBuilder();
				Int32 pos1 = 0;
				Int32 pos2 = 0;
				Int32 pos3 = 0;
				Boolean blnStarted = false;
				String strCurrentSet;
				while (pos2>-1) {
					pos2 = strInput.IndexOf(" ",pos1);
					if (pos2>-1) {
						strCurrentSet = strInput.Substring(pos1,pos2-pos1);
						pos1 = pos2+1;
						}
					else {
						strCurrentSet = strInput.Substring(pos1);
						pos1 = strInput.Length;
						}
					if (strCurrentSet.Length>0) {
						pos3 = strCurrentSet.IndexOf(":");
						if (blnStarted)
							sbWhere.Append(" or ");
						blnStarted = true;
						if (pos3>-1) {
							sbWhere.Append (str1 + " in (" + strCurrentSet.Substring(0,pos3) + ") and " + str2 + " in (" + strCurrentSet.Substring(pos3+1) + ")");
							}
						else
							sbWhere.Append (str2 + " in (" + strCurrentSet + ")");
						}
					}
				return (sbWhere.ToString());
				}
			public String OutputSelectColumns(CommaModes CommaMode) {
				return OutputSelectColumns(CommaMode, TabCount, false);
				}
			public String OutputSelectColumns(CommaModes CommaMode, Boolean UseAlternateExpressions) {
				return OutputSelectColumns(CommaMode, TabCount, UseAlternateExpressions);
				}
			public String OutputSelectColumns(CommaModes CommaMode, Int32 TabCount) {
				return OutputSelectColumns(CommaMode, TabCount, false);
				}
			public String OutputSelectColumns(CommaModes CommaMode, Int32 TabCount, Boolean UseAlternateExpressions) {
				int DimensionCount = 0;
				StringBuilder sb = new StringBuilder();
				if (CommaMode==CommaModes.Before || CommaMode==CommaModes.BeforeAndAfter)
					sb.Append(",");
				foreach (KeyValuePair<String,Dimension> p in Dimensions) {
					Dimension d = p.Value;
					if ((DimensionCount++)>0)
						sb.Append(",");
					sb.Append(d.OutputSelectColumns(TabCount, UseAlternateExpressions));
					}
				if (CommaMode==CommaModes.After || CommaMode==CommaModes.BeforeAndAfter)
					sb.Append(",");
				return sb.ToString();
				}
			public String OutputJoin() {
				return OutputJoin(TabCount);
				}
			public String OutputJoin(Int32 TabCount) {
				StringBuilder sb = new StringBuilder();
				foreach (KeyValuePair<String,Dimension> p in Dimensions) {
					Dimension d = p.Value;
					sb.Append(d.OutputJoin(TabCount));
					}
				return sb.ToString();
				}
			public String OutputJoinExpressions(AndModes AndMode) {
				return OutputJoinExpressions(AndMode, TabCount);
				}
			public String OutputJoinExpressions(AndModes AndMode, Int32 TabCount) {
				Boolean FirstExpression = true;
				StringBuilder sb = new StringBuilder();
				if (Dimensions.Count>0) {
					foreach (KeyValuePair<String,Dimension> p in Dimensions) {
						Dimension d = p.Value;
						if (d.Condition && d.JoinExpression!=null) {
							for (int ii=0;ii<=TabCount;ii++)
								sb.Append("\t");
							if (FirstExpression || AndMode==AndModes.Before) {
								sb.Append("\tand ");
								FirstExpression = false;
								}
							sb.Append(d.JoinExpression);
							}
						}
					}
				return sb.ToString();
				}
			public String OutputFilter(String FilterID, String ListCSV) {
				return Dimensions[FilterID].KeyColumn.OutputFilter(Select.AndModes.Before,ListCSV);
				}
			public String OutputWhere(Boolean WhereClauseStartedAlready) {
				return OutputWhere(WhereClauseStartedAlready, TabCount);
				}
			public String OutputWhere(Boolean WhereClauseStartedAlready, Int32 TabCount) {
				this.WhereFlag = !WhereClauseStartedAlready;
				StringBuilder sb = new StringBuilder();
				if (Dimensions.Count>0) {
					foreach (KeyValuePair<String,Dimension> p in Dimensions) {
						Dimension d = p.Value;
						sb.Append(d.OutputWhere());
						}
					}
				return sb.ToString();
				}
			public String OutputGroupBy(CommaModes CommaMode) {
				return OutputGroupBy(CommaMode, TabCount, false);
				}
			public String OutputGroupBy(CommaModes CommaMode, Int32 TabCount) {
				return OutputGroupBy(CommaMode, TabCount, false);
				}
			public String OutputGroupBy(CommaModes CommaMode, Boolean UseAlternateExpressions) {
				return OutputGroupBy(CommaMode, TabCount, UseAlternateExpressions);
				}
			public String OutputGroupBy(CommaModes CommaMode, Int32 TabCount, Boolean UseAlternateExpressions) {
				StringBuilder sb = new StringBuilder();
				Int32 DimensionCount = 0;
				if (Dimensions.Count>0) {
					foreach (KeyValuePair<String,Dimension> p in Dimensions) {
						Dimension d = p.Value;
						Boolean ConstantExpression = false;
						if (d.KeyColumn!=null) {
							if (UseAlternateExpressions)
								ConstantExpression = d.KeyColumn.KeyExpressionAlternateConstant;
							else
								ConstantExpression = d.KeyColumn.KeyExpressionConstant;
							}
						if (!ConstantExpression && d.Condition) {
							if ((DimensionCount++)>0)
								sb.Append(",");
							else if (CommaMode==CommaModes.Before || CommaMode==CommaModes.BeforeAndAfter)
								sb.Append(",");
							sb.Append(d.OutputGroupBy(TabCount, UseAlternateExpressions));
							}
						}
					if (DimensionCount>0)
						if (CommaMode==CommaModes.After || CommaMode==CommaModes.BeforeAndAfter)
							sb.Append(",");
					}
				if (sb.Length==0 && CommaMode==CommaModes.BeforeAndAfter)
					sb.Append(",");
				return sb.ToString();
				}
			public class KeyDataColumn: DataColumn {
				public Boolean KeyExpressionConstant = false;
				public Boolean KeyExpressionAlternateConstant = false;
				public String PairSetKey = null;
				private Dimension Parent;
				public KeyDataColumn(Dimension Parent, String ColumnName, String PairSetKey, String KeyExpression): base(Parent) {
					this.Parent = Parent;
					this.ColumnName = ColumnName;
					this.PairSetKey = PairSetKey;
					this.KeyExpression = KeyExpression;
					}
				public KeyDataColumn(Dimension Parent, String ColumnName, String KeyExpression): base(Parent) {
					this.Parent = Parent;
					this.ColumnName = ColumnName;
					this.KeyExpression = KeyExpression;
					}
				public KeyDataColumn(Dimension Parent): base(Parent) {
					this.Parent = Parent;
					this.PairSetKey = null;
					}
				public String KeyExpression {
					get { return base.ThenExpression; }
					set { base.ThenExpression = value; }
					}
				public String KeyExpressionAlternate {
					get { return base.ThenExpressionAlternate; }
					set { base.ThenExpressionAlternate = value; }
					}
				public String ParsePairSet(String PairSetListCSV) {
					if (PairSetKey==null)
						return "(" + KeyExpression + " in (" + PairSetListCSV + "))";
					else
						return ParsePairSetOutput(PairSetListCSV, PairSetKey, KeyExpression);
					}
				public String OutputFilter(Select.AndModes AndMode, String PairSetListCSV) {
					StringBuilder sb = new StringBuilder();
					if (PairSetListCSV.Length>0) {
						for (int ii=0;ii<=Parent.Parent.TabCount;ii++)
							sb.Append("\t");
						if (AndMode==AndModes.Before)
							sb.Append("and ");
						sb.Append(ParsePairSet(PairSetListCSV));
						}
					return sb.ToString();
					}
				private string ParsePairSetOutput(string strInput, string str1, string str2) {
					System.Text.StringBuilder sbWhere = new System.Text.StringBuilder();
					int pos1 = 0;
					int pos2 = 0;
					int pos3 = 0;
					bool blnStarted = false;
					string strCurrentSet;
					while (pos2>-1) {
						pos2 = strInput.IndexOf(" ",pos1);
						if (pos2>-1) {
							strCurrentSet = strInput.Substring(pos1,pos2-pos1);
							pos1 = pos2+1;
							}
						else {
							strCurrentSet = strInput.Substring(pos1);
							pos1 = strInput.Length;
							}
						if (strCurrentSet.Length>0) {
							pos3 = strCurrentSet.IndexOf(":");
							if (blnStarted)
								sbWhere.Append(" or ");
							blnStarted = true;
							if (pos3>-1) {
								sbWhere.Append (str1 + " in (" + strCurrentSet.Substring(0,pos3) + ") and " + str2 + " in (" + strCurrentSet.Substring(pos3+1) + ")");
								}
							else
								sbWhere.Append (str2 + " in (" + strCurrentSet + ")");
							}
						}
					return (sbWhere.ToString());
					}
				}
			public class DataColumn {
				public DataColumn(Dimension Parent) {
					this.Parent = Parent;
					}
				private Dimension Parent = null;
				public class JoinList: List<Join> {}
				public class Join {
					public enum JoinTypes {Inner, LeftOuter, RightOuter, Cross}
					public JoinTypes JoinType;
					public String TableExpression;
					public String JoinExpression;
					}
				public JoinList Joins = new JoinList();
				public String ColumnName = null;
				public String ThenExpression = "null";
				public String ElseExpression = "null";
				public String ThenExpressionAlternate = "null";
				public String ElseExpressionAlternate = "null";
				public String OutputSelectColumns(Boolean UseAlternateExpressions) {
					return OutputSelectColumns(-1,UseAlternateExpressions);
					}
				public String OutputSelectColumns(Int32 TabCount, Boolean UseAlternateExpressions) {
					Int32 tc;
					if (TabCount<0)
						tc = Parent.Parent.TabCount;
					else
						tc = TabCount;
					StringBuilder sb = new StringBuilder();
					sb.Append("\n");
					for (int ii=0;ii<=tc;ii++)
						sb.Append("\t");
					if (Parent.Condition)
						if (UseAlternateExpressions && ThenExpressionAlternate!="null")
							sb.Append(ThenExpressionAlternate + " as " + ColumnName);
						else
							sb.Append(ThenExpression + " as " + ColumnName);
					else
						if (UseAlternateExpressions && ElseExpressionAlternate!="null")
							sb.Append(ElseExpressionAlternate + " as " + ColumnName);
						else
							sb.Append(ElseExpression + " as " + ColumnName);
					return sb.ToString();
					}
				public String OutputJoin(Int32 TabCount) {
					Int32 tc;
					if (TabCount<0)
						tc = Parent.Parent.TabCount;
					else
						tc = TabCount;
					StringBuilder sb = new StringBuilder();
					if (Parent.Condition) {
						if (Joins.Count>0) {
							sb.Append("\n");
							for (int ii=0;ii<tc;ii++)
								sb.Append("\t");
							foreach (Join jn in Joins) {
								switch (jn.JoinType) {
									case Join.JoinTypes.Inner:
										sb.Append("inner join\n");
										break;
									case Join.JoinTypes.LeftOuter:
										sb.Append("left outer join\n");
										break;
									case Join.JoinTypes.RightOuter:
										sb.Append("right outer join\n");
										break;
									case Join.JoinTypes.Cross:
										sb.Append("cross join\n");
										break;
									}
								for (int ii=0;ii<=Parent.Parent.TabCount;ii++)
									sb.Append("\t");
								sb.Append(jn.TableExpression);
								if (jn.JoinType!=Join.JoinTypes.Cross) {
									sb.Append("\n");
									for (int ii=0;ii<Parent.Parent.TabCount;ii++)
										sb.Append("\t");
									sb.Append("on " + jn.JoinExpression);
									}
								}
							}
						}
					return sb.ToString();
					}
				public String OutputGroupBy(Boolean UseAlternateExpressions) {
					return OutputGroupBy(-1,UseAlternateExpressions);
					}
				public String OutputGroupBy(Int32 TabCount, Boolean UseAlternateExpressions) {
					Int32 tc;
					if (TabCount<0)
						tc = Parent.Parent.TabCount;
					else
						tc = TabCount;
					StringBuilder sb = new StringBuilder();
					if (Parent.Condition) {
						sb.Append("\n");
						for (int ii=0;ii<=tc;ii++)
							sb.Append("\t");
						if (UseAlternateExpressions && ThenExpressionAlternate!="null")
							sb.Append(ThenExpressionAlternate);
						else
							sb.Append(ThenExpression);
						}
					return sb.ToString();
					}
				}
			public class Dimension {
				public class DataColumnList: List<DataColumn> {}
				public Boolean Frozen = false;
				public KeyDataColumn KeyColumn;
				public DataColumnList DataColumns = new DataColumnList();
				private Boolean condition = false;
				public delegate void ConditionChangedHandler(object source, EventArgs e);
				public delegate void ConditionAccessedHandler(object source, EventArgs e);
				public event ConditionChangedHandler ConditionChanged;
				public event ConditionChangedHandler ConditionAccessed;
				public Boolean Condition {
					get {
						if (ConditionAccessed!=null) {
							ConditionAccessed(this,new EventArgs());
							}
						return this.condition;
						}
					set {
						this.condition = (value || Frozen);
						if (ConditionChanged!=null)
							ConditionChanged(this,new EventArgs());
						}
					}
				public Select Parent = null;
				public String JoinExpression = null;
				public Dimension (Select Parent) {
					this.Parent = Parent;
					}
				public class WhereList: List<WhereList.Where> {
					public class Where {
						public Boolean Condition { get; set; }
						public String WhereExpression = null;
						public Where (Boolean Condition) {
							this.Condition = Condition;
							} 
						}
					}
				public WhereList WhereObjects = new WhereList();
				public String OutputSelectColumns(Int32 TabCount, Boolean UseAlternateExpressions) {
					StringBuilder sb = new StringBuilder();
					if (KeyColumn!=null)
						sb.Append(KeyColumn.OutputSelectColumns(TabCount, UseAlternateExpressions));
					foreach (DataColumn dc in DataColumns) {
						if (sb.Length>0)
							sb.Append(",");
						sb.Append(dc.OutputSelectColumns(TabCount, UseAlternateExpressions));
						}
					return sb.ToString();
					}
				public String OutputJoin(Int32 TabCount) {
					StringBuilder sb = new StringBuilder();
					if (Condition) {
						if (KeyColumn!=null)
							sb.Append(KeyColumn.OutputJoin(TabCount));
						foreach (DataColumn dc in DataColumns) {
							sb.Append(dc.OutputJoin(TabCount));
							}
						}
					return sb.ToString();
					}
				public String OutputWhere() {
					StringBuilder sb = new StringBuilder();
					foreach (Select.Dimension.WhereList.Where wh in WhereObjects) {
						if (wh.Condition) {
							if (this.Parent.WhereFlag) {
								for (int ii=0;ii<Parent.TabCount;ii++)
									sb.Append("\t");
								sb.Append("where ");
								this.Parent.WhereFlag = false;
								}
							else {
								sb.Append("and ");
								for (int ii=0;ii<=Parent.TabCount;ii++)
									sb.Append("\t");
								}
							sb.Append(" (" + wh.WhereExpression + ")");
							}
						}
					return sb.ToString();
					}
				public String OutputGroupBy(Int32 TabCount, Boolean UseAlternateExpressions) {
					StringBuilder sb = new StringBuilder();
					if (KeyColumn!=null)
						sb.Append(KeyColumn.OutputGroupBy(TabCount, UseAlternateExpressions));
					foreach (DataColumn dc in DataColumns) {
						if (sb.Length>0)
							sb.Append(",");
						sb.Append(dc.OutputGroupBy(TabCount, UseAlternateExpressions));
						}
					return sb.ToString();
					}
				}
			}
		protected string ParsePairSetOutput(string strInput, string str1, string str2) {
			System.Text.StringBuilder sbWhere = new System.Text.StringBuilder();
			int pos1 = 0;
			int pos2 = 0;
			int pos3 = 0;
			bool blnStarted = false;
			string strCurrentSet;
			while (pos2>-1) {
				pos2 = strInput.IndexOf(" ",pos1);
				if (pos2>-1) {
					strCurrentSet = strInput.Substring(pos1,pos2-pos1);
					pos1 = pos2+1;
					}
				else {
					strCurrentSet = strInput.Substring(pos1);
					pos1 = strInput.Length;
					}
				if (strCurrentSet.Length>0) {
					pos3 = strCurrentSet.IndexOf(":");
					if (blnStarted)
						sbWhere.Append(" or ");
					blnStarted = true;
					if (pos3>-1) {
						sbWhere.Append (str1 + " in (" + strCurrentSet.Substring(0,pos3) + ") and " + str2 + " in (" + strCurrentSet.Substring(pos3+1) + ")");
						}
					else
						sbWhere.Append (str2 + " in (" + strCurrentSet + ")");
					}
				}
			return (sbWhere.ToString());
			}
		protected string FixSingleQuotesForSQL(string strInput) {
			int lngPos;
			string strOutput;
			strOutput = strInput;
			lngPos = 0;
			if (strInput!=null) {
				lngPos = strOutput.IndexOf("'");
				while (lngPos>-1) {
					strOutput = strOutput.Substring(0,lngPos+1) + "'" +strOutput.Substring(lngPos+1);
					lngPos = strOutput.IndexOf("'",lngPos+2);
					}
				}
			return (strOutput);
			}
		public string ConnectionString = "";
		public String ParseStringAddStoreGroup(string strInputString, string strStoreGroup) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParseStringAddStoreGroup('" + strInputString + "','" + FixSingleQuotesForSQL(strStoreGroup) + "') as StoreGroupList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["StoreGroupList"];
			}
		public String ParsePairSetStringAddProfitCenter(string strInputString, string strProfitCenter) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddProfitCenter('" + strInputString + "','" + FixSingleQuotesForSQL(strProfitCenter) + "') as ProfitCenterList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["ProfitCenterList"];
			}
		public String ParsePairSetStringAddMainGroup(string strInputString, string strMainGroup) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddMainGroup('" + strInputString + "','" + FixSingleQuotesForSQL(strMainGroup) + "') as MainGroupList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["MainGroupList"];
			}
		public String ParsePairSetStringAddSaleType(string strInputString, string strSaleType) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddSaleType('" + strInputString + "','"
				+ FixSingleQuotesForSQL(strSaleType) + "') as SaleTypeList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["SaleTypeList"];
			}
		public String ParsePairSetStringAddStaffName(string strInputString, string strStaffName) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddStoreStaff('" + strInputString + "','" + FixSingleQuotesForSQL(strStaffName) + "') as StaffList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["StaffList"];
			}
		public String ParsePairSetStringAddManagerName(string strInputString, string strManagerName) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddManager('" + strInputString + "','" + FixSingleQuotesForSQL(strManagerName) + "') as ManagerList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["ManagerList"];
			}
		public String ParsePairSetStringAddMenuItem(string strInputString, string strMenuItem) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			strMenuItem = strMenuItem.Replace("Menu Item: ","");
			string strSQL = "select dbo.fn_ParsePairSetStringAddMenuItem('" + strInputString + "','" + FixSingleQuotesForSQL(strMenuItem) + "') as MenuItemList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["MenuItemList"];
			}
		public String ParsePairSetStringAddVoidReason(string strInputString, string strVoidReason) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddVoidReason('" + strInputString + "','" + FixSingleQuotesForSQL(strVoidReason) + "') as VoidReasonList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["VoidReasonList"];
			}
		public String ParsePairSetStringAddMenuItemPLU(string strInputString, string strMenuItemPLU) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddMenuItemPLU('" + strInputString + "','" + strMenuItemPLU.ToString() + "') as MenuItemPLUList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["MenuItemPLUList"];
			}
		public String ParsePairSetStringAddDiscount(string strInputString, string strDiscount) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddDiscount('" + strInputString + "','" + FixSingleQuotesForSQL(strDiscount) + "') as DiscountList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["DiscountList"];
			}
		public String ParsePairSetStringAddFrequentDinerPlan(string strInputString, string strFrequentDinerPlan) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddFrequentDinerPlan('" + strInputString + "','" + FixSingleQuotesForSQL(strFrequentDinerPlan) + "') as FrequentDinerPlanList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["FrequentDinerPlanList"];
			}
		public String ParsePairSetStringAddFrequentDinerCustomer(string strInputString, string strFrequentDinerCustomer) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddFrequentDinerCustomer('" + strInputString + "','" + FixSingleQuotesForSQL(strFrequentDinerCustomer) + "') as FrequentDinerCustomerList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["FrequentDinerCustomerList"];
			}
		public String ParsePairSetStringAddStoreStaffName(string strInputString, string strStoreStaffName) {
			string strConnection = ConnectionString;
			SqlConnection cn = new SqlConnection(strConnection);
			cn.ConnectionString = strConnection;
			string strSQL = "select dbo.fn_ParsePairSetStringAddStoreStaff('" + strInputString + "','" + FixSingleQuotesForSQL(strStoreStaffName) + "') as StoreStaffList";
			SqlDataAdapter adpt = new SqlDataAdapter(strSQL, cn);
			DataSet ds = new DataSet();
			adpt.Fill(ds, "tmp");
			DataView dv = new DataView(ds.Tables["tmp"]);
			return (string)dv[0]["StoreStaffList"];
			}
		public String ParsePairSetStringAddCheckNumber(string strInputString, string strCheckNumber) {
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

