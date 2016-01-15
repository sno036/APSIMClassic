﻿using System;
using System.Web.UI.WebControls;
using System.Net;
using System.IO;
using RestSharp;
using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace BobWeb
{
    public partial class Upload : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            RestClient client = new RestClient("https://www.apsim.info/REST/api");
            RestRequest req = new RestRequest();
            req.Resource = "BT";
            IRestResponse response = client.Execute(req);
            string xml = response.Content;
            xml = xml.Substring(1, xml.Length - 2);
            DataSet ds = new DataSet();
            DataTable Data;
            using (StringReader sr = new StringReader(xml))
            {
                ds.ReadXml(sr);
                Data = ds.Tables[0];
            }

            List<string> tasks = Data.AsEnumerable().Select(dt => dt[0].ToString()).ToList();
            BugList.DataSource = tasks;
            BugList.DataBind();
        }

        protected void UploadButton_Click(object sender, EventArgs e)
        {
            bool Ok = AuthenticateSVNUser(UserNameTextBox.Text, PasswordTextBox.Text);
            InvalidLabel.Visible = !Ok;
            DescriptionLabel.Visible = DescriptionTextBox.Text == "";
            PatchLabel.Visible = FileUpload.FileName == "";
            if (Ok && DescriptionTextBox.Text != "" && FileUpload.FileName != "")
            {
                // Check the bug id.
                int PosHyphen = BugList.Text.IndexOf(" - ");
                if (PosHyphen == -1)
                    throw new Exception("Bad BugID description: " + BugList.Text);
                int BugID = Convert.ToInt32(BugList.Text.Substring(0, PosHyphen));

                // Write the file to the upload directory.
                string FileNameToWrite = "C:\\inetpub\\wwwroot\\Files\\Upload\\" + Path.GetFileNameWithoutExtension(FileUpload.FileName).Replace(" ", "") + "(" + DateTime.Now.ToString("dd-MM-yyyy_HH.mm.ss") + ").zip";

                // Write the zip file.
                FileStream ZipFile = new FileStream(FileNameToWrite, FileMode.CreateNew);
                FileUpload.FileContent.CopyTo(ZipFile);
                ZipFile.Close();

                ApsimBuildsDB BuildsDB = new ApsimBuildsDB();
                BuildsDB.Open();
                BuildsDB.Add(UserNameTextBox.Text, PasswordTextBox.Text, FileNameToWrite, DescriptionTextBox.Text, BugID, CheckBox.Checked);
                BuildsDB.Close();
                Response.Redirect("http://bob.apsim.info/BobWeb/Bob.aspx");
            }

        }

        /// <summary>
        /// Perorms an authorisation check on the given username nad password. Returns true
        /// if user and password is OK.
        /// </summary>
        private bool AuthenticateSVNUser(string UserName, string Password)
        {
            bool Ok = false;
            try
            {
                string uri = "http://apsrunet.apsim.info/svn/apsim/";

                HttpWebRequest request = (HttpWebRequest) System.Net.WebRequest.Create(uri);
                request.Method = "MKACTIVITY";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Credentials = new NetworkCredential(UserName, Password); ;
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception ex)
            {
                Ok = !ex.Message.Contains("Unauthorized");
            }
            return Ok;
        }


  
    }
}