﻿using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using System.Windows.Forms;
using Liphsoft.Crypto.Argon2;

namespace ClimbingWall
{
    public class Database
    {
        MySqlConnection connection;
        private static Database instance;
       // int currentEmployee; // Employee currently logged in DEPRECIATED: CURRENTLY Globals

        public void connect()
        {
            MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder();
            builder.Server = Globals.DBPort;
            builder.UserID = Globals.DBID;
            builder.Password = Globals.DBPassword;
            builder.Database = Globals.DBName;
            connection = new MySqlConnection(builder.ToString());
            try
            {
                connection.Open();
            }
            catch (Exception)
            {
                MessageBox.Show("Connection Failed");
            }
        }
        ~Database()
        {
            connection.Close();
        }

        public static Database Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Database();
                }
                return instance;
            }
        }
        public bool login(string username, string password, ref EmployeeLevel empLevel)
        {
            string cmd_str = "SELECT * FROM climbing_wall.employee WHERE Employee_Username = @username";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@username", username);

            // Create hasher variable to hash the entered password
            var hasher = new PasswordHasher();
            // Cross-check hashed password with hashes in database
            // If match is found
            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            if (reader.HasRows)
            {
                reader.Read();
                if (!hasher.Verify(reader.GetString("Password"), password))
                {
                    reader.Close();
                    return false;
                }
                Globals.empLevel = (EmployeeLevel)reader.GetInt16("Level");
				Globals.currEmployeeId = reader.GetInt16("Employee_ID");
				Globals.currEmployeeName = reader.GetString("Employee_Username");
            }
            else
            {
                reader.Close();
                return false;
            }
            reader.Close();
            return true;
        }
        public PatronLoginStatus patron_login(string ID)
        {
            DateTime dt = DateTime.Now;
            int suspColNum = 13;
            int first_log;
            int userID = Int32.Parse(ID);
            string cmd_str = "SELECT * FROM climbing_wall.patron WHERE PatronID = @ID";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@ID", userID);

            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return PatronLoginStatus.MISCERROR;
            }
            if(!reader.HasRows)
            {
                reader.Close();
                return PatronLoginStatus.PATRONNOTFOUND;
            }
            reader.Read();
            if (!reader.IsDBNull(suspColNum))
            {
                int suspID = reader.GetInt16("FK_Suspend");
				reader.Close();
                PatronLoginStatus status = checkSuspensions(suspID, ref reader);
                if(status != PatronLoginStatus.SUCCESS)
                {
                    return status;
                }
				if(status == PatronLoginStatus.SUCCESS) {
					return PatronLoginStatus.PATRONSUSPCLEAR;
				}
            }
			reader.Read();
            DateTime waverExp;
            try
            {
                waverExp = reader.GetDateTime("WaiverExp");
            }
            catch
            {
                waverExp = DateTime.MinValue;
            }
            DateTime currentDate = DateTime.Today;
            PatronLoginStatus waverStatus = PatronLoginStatus.SUCCESS;
            if (waverExp == DateTime.MinValue || currentDate.Date.CompareTo(waverExp) > 0)
            {
                waverStatus = PatronLoginStatus.WAVEREXPIRED;
            }
            if (waverStatus != PatronLoginStatus.SUCCESS)
            {
                reader.Close();
                return waverStatus;
            }
            reader.Close();

            cmd_str = "insert into climbing_wall.log_table (FK_Patron_ID, Log_DateTime) VALUES (@id, @dt)";
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@id", userID);
            cmd.Parameters.AddWithValue("@dt", dt);
            try
            {
                cmd.ExecuteNonQuery();

            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return PatronLoginStatus.MISCERROR;
            }
            
            long lastInsert = cmd.LastInsertedId;
            cmd_str = "SELECT * FROM climbing_wall.patron WHERE PatronID = @ID";
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@ID", userID);
                reader = cmd.ExecuteReader();
            var ordinal = reader.GetOrdinal("FK_FirstLog");
            reader.Read();
            if (reader.IsDBNull(ordinal))
                first_log = (int)lastInsert;
            else
                first_log = reader.GetInt32("FK_FirstLog");
            reader.Close();

            cmd_str = "UPDATE `climbing_wall`.`patron` SET `New_Flag`='0', `FK_FirstLog` = @fLog, `FK_LatestLog` = @lLog WHERE `PatronID` = @id";
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@id", userID);
            cmd.Parameters.AddWithValue("@flog", first_log);
            cmd.Parameters.AddWithValue("@lLog", (int)lastInsert);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return PatronLoginStatus.MISCERROR;
            }
            return PatronLoginStatus.SUCCESS;
        }

        public PatronLoginStatus checkSuspensions(int suspID, ref MySqlDataReader suspReader)
        {
            string cmd_str = "SELECT * FROM climbing_wall.suspensions WHERE Suspend_ID = @ID";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@ID", suspID);
            try
            {
                suspReader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return PatronLoginStatus.MISCERROR;
            }
            suspReader.Read();
            DateTime currentDate = DateTime.Today;
            DateTime expires = suspReader.GetDateTime("Expires");
            if (suspReader.GetBoolean("Active") && currentDate.Date.CompareTo(expires) < 0)
            {
                suspReader.Close();
                return PatronLoginStatus.PATRONSUSPENDED;
            }
            else
            {
                suspReader.Close();
                cmd_str = "UPDATE climbing_wall.patron SET FK_SUSPEND = NULL WHERE FK_Suspend = @id";
                cmd = new MySqlCommand(cmd_str, connection);
                cmd.CommandText = cmd_str;
                cmd.Parameters.AddWithValue("@id", suspID);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (MySqlException ex)
                {
                    MessageBox.Show(ex.Message);
                    return PatronLoginStatus.MISCERROR;
                }
                ////Note that all of this related to the active variable is technically useless-- remove it?
                cmd_str = "UPDATE climbing_wall.suspensions SET Active = 0 WHERE Suspend_ID = @id";
                cmd = new MySqlCommand(cmd_str, connection);
                cmd.CommandText = cmd_str;
                cmd.Parameters.AddWithValue("@id", suspID);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (MySqlException ex)
                {
                    MessageBox.Show(ex.Message);
                    return PatronLoginStatus.MISCERROR;
                }
            }
            return PatronLoginStatus.SUCCESS;
        }

        public bool createPatron(string fName, string lName, string midI, string phone, string email, int pat_Id, byte[] img)
        {
            DateTime curDate = DateTime.Today;
            DateTime expDate = curDate.AddYears(1);
            bool status = true;
            string cmd_str = "INSERT INTO climbing_wall.patron (PatronID, FName, LName, MInitial, Email, Phone, New_Flag, Waiver, WaiverExp) VALUES (@PatronID, @Fname, @LName, @MI, @Email, @Phone, @New, @waiver, @date)";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@Fname", fName);
            cmd.Parameters.AddWithValue("@LName", lName);
            cmd.Parameters.AddWithValue("@MI", midI);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@PatronID", pat_Id);
            cmd.Parameters.AddWithValue("@New", 1);
            cmd.Parameters.AddWithValue("@waiver", img);
            cmd.Parameters.AddWithValue("@date", expDate);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                status = false;
            }

            return status;
        }

        public bool modifyPatron(string fName, string lName, string midI, string phone, string email, int pat_Id, byte[] img)
        {
            bool patronExists = false;
            string cmd_str = "SELECT * FROM climbing_wall.patron WHERE PatronID = @ID";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@ID", pat_Id);

            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            if (reader.HasRows)
            {
                patronExists = true;
            }
            reader.Close();
            
            if (patronExists)
            {
                cmd_str = "UPDATE climbing_wall.patron SET FName=@Fname, LName=@LName, MInitial=@MI, Email=@Email, Phone=@Phone, Waiver = @waiver, WaiverExp = @exp where PatronID = @PatronID";
            }
            else
            {
                return false;
            }
            DateTime expire = DateTime.Now;
            expire = expire.AddYears(1);
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@Fname", fName);
            cmd.Parameters.AddWithValue("@LName", lName);
            cmd.Parameters.AddWithValue("@MI", midI);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@PatronID", pat_Id);
            cmd.Parameters.AddWithValue("@waiver", img);
            cmd.Parameters.AddWithValue("@exp", expire);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }

            return true;
        }

        public bool createEmployee(string firstName, string lastName, string username, string password, EmployeeLevel empLevel)
        {
            var hasher = new PasswordHasher();
            string hashedPassword = hasher.Hash(password);
            string cmd_str = "INSERT INTO climbing_wall.employee (Emp_FirstName, Emp_LastName, Employee_Username, Password, Level) VALUES (@firstName, @lastName, @username, @hashedPassword, @level)";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@firstName", firstName);
            cmd.Parameters.AddWithValue("@lastName", lastName);
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@hashedPassword", hashedPassword);
            cmd.Parameters.AddWithValue("@level", empLevel);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            return true;
        }

        public SuspendCode suspendPatron(int patronID, string reason, SuspendLength length)
        {
            //Get suspension length
            DateTime dt = getExpirationDate(length);

            string cmd_str = "SELECT * FROM climbing_wall.patron WHERE PatronID = @ID";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@ID", patronID);
            int suspendCount = 0;
            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return SuspendCode.DB_FAIL;
            }
       
            if (reader.HasRows)
            {
                reader.Read();
                suspendCount = reader.GetInt16("SuspendCount");
                var ordinal = reader.GetOrdinal("FK_Suspend");
                if (!reader.IsDBNull(ordinal))
                {
                    reader.Close();
                    return SuspendCode.SUSPEND_EXISTS;
                }
            }
            else
            {
                reader.Close();
                return SuspendCode.PATRON_NOT_EXIST;
            }
            reader.Close();

            cmd_str = "INSERT INTO climbing_wall.suspensions (FK_Patron_ID, Active, Reason, Expires) VALUES (@patron, @active, @reason, @expires)";
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@patron", patronID);
            cmd.Parameters.AddWithValue("@active", 1);
            cmd.Parameters.AddWithValue("@reason", reason);
            cmd.Parameters.AddWithValue("@expires", dt);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return SuspendCode.DB_FAIL;
            }
            long lastInsert = cmd.LastInsertedId;
            cmd_str = "UPDATE `climbing_wall`.`patron` SET `FK_Suspend` = @fk, `SuspendCount` = @count WHERE `PatronID` = @id";
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@fk", (int)lastInsert);
            cmd.Parameters.AddWithValue("@count", suspendCount + 1);
            cmd.Parameters.AddWithValue("@id", patronID);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return SuspendCode.DB_FAIL;
            }
            return SuspendCode.SUCCESS;
        }
        DateTime getExpirationDate(SuspendLength length)
        {
            DateTime dt = DateTime.Now;
            DateTime spring = new DateTime(dt.Year, 5, 12);
            DateTime summer = new DateTime(dt.Year, 8, 21);
            DateTime fall = new DateTime(dt.Year, 12, 20);
            if (length == SuspendLength.SEVEN_DAYS)
                dt = dt.AddDays(7);
            else if (length == SuspendLength.THIRTY_DAYS)
                dt = dt.AddDays(7);
            else if (length == SuspendLength.SEMESTER)
            {
                if (DateTime.Compare(dt,spring) < 0) // spring
                {
                    dt.Add(spring.Subtract(dt));
                }
                else if (DateTime.Compare(dt, summer) < 0) // summer
                {
                    dt.Add(summer.Subtract(dt));
                }
                else
                {
                    dt.Add(fall.Subtract(dt));
                }
            }
            else
            {
                dt.AddYears(1);
            }
            return dt;
        }
        
        public bool changePassword(string current, string _new)
        {
            // Create hasher variable to hash the new password
            var hasher = new PasswordHasher();
            string hashedNew = hasher.Hash(_new);

            string cmd_str = "SELECT * FROM climbing_wall.employee WHERE Employee_ID = @id";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@id", Globals.currEmployeeId);

            // Cross-check hashed password with hashes in database
            // If match is found
            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            if (reader.HasRows)
            {
                reader.Read();
                if (!hasher.Verify(reader.GetString("Password"), current))
                {
                    reader.Close();
                    return false;
                }
            }
            else
            {
                reader.Close();
                return false;
            }
            reader.Close();

            // Change Password
            cmd_str = "UPDATE climbing_wall.employee SET `Password` = @hashedPassword WHERE `Employee_ID` = @id";
            cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@id", Globals.currEmployeeId);
            cmd.Parameters.AddWithValue("@hashedPassword", hashedNew);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            return true;
        }

        public bool addNote(string text, int priority)
        {
            string cmd_str = "INSERT INTO climbing_wall.note (FK_Emp_Id, Note_Text, Note_DateTime, Note_Priority) VALUES (@user, @text, @date, @priority)";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@user", Globals.currEmployeeId);
            cmd.Parameters.AddWithValue("@text", text);
            cmd.Parameters.AddWithValue("@date", DateTime.Now);
            cmd.Parameters.AddWithValue("@priority", priority);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            return true;
        }
        public void resetPassword(string name)
        {
            string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            string newPass = "";
            Random rand = new Random();
            var hasher = new PasswordHasher();
            // Create random password
            for (int i = 0; i < 10; i++)
            {
                newPass += chars[rand.Next(0, chars.Length)];
            }

            // Change password
            string cmd_str = "UPDATE climbing_wall.employee SET `Password` = @hashedPassword WHERE `Employee_Username` = @name";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@hashedPassword", hasher.Hash(newPass));
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            MessageBox.Show("New password: " + newPass);
        }
		public DataTable searchDatabase(string tableName)			//just collect all the data from the table
		{
			string cmd_str = "select * from " + tableName + " ;";
			MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
			cmd.CommandText = cmd_str;

			MySqlDataAdapter sda = new MySqlDataAdapter();
			DataTable dataset = new DataTable();
			try {
				sda.SelectCommand = cmd;
				sda.Fill(dataset);
				sda.Update(dataset);
			}
			catch (MySqlException ex) {
				MessageBox.Show(ex.Message);
				return null;
			}

			return dataset;
		}
		public DataTable searchDatabase(string tableName, string whereStatement)
        {
            string cmd_str;
            if (tableName != "climbing_wall.employee")
            {
                cmd_str = "select * from " + tableName + " " + whereStatement;
            }
            else
            {
                cmd_str = "select Employee_ID, Level, Employee_Username from " + tableName + " " + whereStatement;
            }
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            
            MySqlDataAdapter sda = new MySqlDataAdapter();
            DataTable dataset = new DataTable();
            try
            {
                sda.SelectCommand = cmd;
                sda.Fill(dataset);
                sda.Update(dataset);
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return null;
            }

            return dataset;
        }

		public DataTable searchDatabase(string tableName, string fields ,string whereStatement) //Overloaded statment to check for certain fields
		{
			string cmd_str = "select " + fields + " from " + tableName + " " + whereStatement;
			//MessageBox.Show(cmd_str);   //unCommneting for debugging
			MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
			cmd.CommandText = cmd_str;

			MySqlDataAdapter sda = new MySqlDataAdapter();
			DataTable dataset = new DataTable();
			try {
				sda.SelectCommand = cmd;
				sda.Fill(dataset);
				sda.Update(dataset);
			}
			catch (MySqlException ex) {
				MessageBox.Show(ex.Message);
				return null;
			}

			return dataset;
		}

		public bool nonQuery(string cmd_str)
        {
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            return true;
        }
        
        public int getEmployeeID(string emp_name)
        {
            string cmd_str = "SELECT * FROM climbing_wall.employee WHERE Employee_Username = @name";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@name", emp_name);
            int empID;

            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return -1;
            }
            if (reader.HasRows)
            {
                reader.Read();
                empID = reader.GetInt32("Employee_Id");
            }
            else
            {
                empID = -1;
            }
            reader.Close();
            return empID;
        }

        public string getEmployeeName(int emp_ID)
        {
            string cmd_str = "SELECT * FROM climbing_wall.employee WHERE Employee_Id = @id";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;
            cmd.Parameters.AddWithValue("@id", emp_ID);
            string empName;

            MySqlDataReader reader;
            try
            {
                reader = cmd.ExecuteReader();
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return "";
            }
            if (reader.HasRows)
            {
                reader.Read();
                empName = reader.GetString("Employee_Username");
            }
            else
            {
                empName = "";
            }
            reader.Close();
            return empName;
        }
		
        public DataTable getNotes()
        {
            string cmd_str = "SELECT FK_Emp_ID, Note_Text, Note_DateTime FROM climbing_wall.note;";
            MySqlCommand cmd = new MySqlCommand(cmd_str, connection);
            cmd.CommandText = cmd_str;

            MySqlDataAdapter sda = new MySqlDataAdapter();
            DataTable dataset = new DataTable();
            try
            {
                sda.SelectCommand = cmd;
                sda.Fill(dataset);
                sda.Update(dataset);
            }
            catch (MySqlException ex)
            {
                MessageBox.Show(ex.Message);
                return null;
            }

            dataset.Columns[0].ColumnName = "User";
            dataset.Columns[1].ColumnName = "Note";
            DataTable dtCloned = dataset.Clone();
            dtCloned.Columns["User"].DataType = System.Type.GetType("System.String");
            foreach (DataRow row in dataset.Rows)
            {
                dtCloned.ImportRow(row);
            }

            
            for (int i =0; i < dtCloned.Rows.Count; i++)
            {
                dtCloned.Rows[i][0] = getEmployeeName(Int32.Parse(dataset.Rows[i][0].ToString()));
            }
            return dtCloned;
        }

    }
}
