#r "SendGrid"
#r "System.Data"

using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using SendGrid.Helpers.Mail;

public static SendGridMessage Run(TimerInfo myTimer, string myInputBlob, out string myOutputBlob, ILogger log)
{
    string DbServer = Environment.GetEnvironmentVariable("SQLDBFailoverMonitor_Server");
    string DbUser = Environment.GetEnvironmentVariable("SQLDBFailoverMonitor_User");
    string DbPassword = Environment.GetEnvironmentVariable("SQLDBFailoverMonitor_Password");
    string ToAddress = Environment.GetEnvironmentVariable("SQLDBFailoverMonitor_NotifyAddress");
    string dateTimeFormat = "yyyy-MM-dd HH:mm";
    SendGridMessage message = null;

    //Get last check datetime
    DateTime lastCheck;
    if (!DateTime.TryParse(myInputBlob, out lastCheck))
    {
        log.LogInformation($"Was not possible to parse myInputBlob");
        lastCheck = DateTime.UtcNow.AddHours(-1);
    };
    log.LogInformation($"lastCheck: {lastCheck}");
    myOutputBlob = lastCheck.ToString();
    
    SqlConnection conn = new SqlConnection(string.Format("Server=tcp:{0}.database.windows.net,1433;Initial Catalog=master;Persist Security Info=False;User ID={1};Password={2};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;", DbServer, DbUser, DbPassword));
    SqlCommand cmd = new SqlCommand("select name, role_desc, modify_date from sys.geo_replication_links rl inner join sys.databases db on rl.database_id = db.database_id", conn);
    SqlDataReader rdr = null;
    StringBuilder sb = new StringBuilder();

    try
    {
        conn.Open();
        rdr = cmd.ExecuteReader();
        
        while (rdr.Read())
        {
            string name = rdr.GetString(0);
            string role_desc = Convert.ToString(rdr.GetValue(1));
            DateTime modify_date = DateTime.Parse(Convert.ToString(rdr.GetValue(2)));
            log.LogInformation(modify_date.ToString());
            log.LogInformation(lastCheck.ToString());
            if(modify_date > lastCheck)
            sb.AppendLine($"<tr><th style='background-color: #E74C3C;'>Role on GeoReplication link in database {name} changed to {role_desc}</th></tr>");
        }

        log.LogInformation($"sb.Length: {sb.Length}");
        log.LogInformation($"sb.ToString: {sb.ToString()}");
        
        if (sb.Length > 0)
        {
            StringBuilder emailContentSB = new StringBuilder();
            emailContentSB.Append(@"<style>
table { border-collapse: collapse; width: 100%;}
th, td { text-align: left; padding: 8px;}
tr:nth-child(even){background-color: #f2f2f2}
th { color: white;}
</style>
<div style='overflow-x:auto;'>");
            emailContentSB.Append($"<h3>Failover alert at {DateTime.UtcNow.ToString(dateTimeFormat)} UTC</h3>");
            emailContentSB.Append($"Last check at {lastCheck.ToString("s")}</h3><br/>");
            emailContentSB.Append(@"<table >");
            emailContentSB.Append(sb.ToString());
            emailContentSB.Append(@"</table></div>");

            message = new SendGridMessage() { Subject = "SQL DB failover alert" };
            message.AddTo(ToAddress);
            message.AddContent("text/html", emailContentSB.ToString());
            myOutputBlob = DateTime.UtcNow.ToString();
        }
    }
    catch (Exception ex)
    {
        log.LogError($"Exception: {ex.Message}");
    }
    finally
    {
        if (rdr != null) { rdr.Close(); }
        if (conn != null) { conn.Close(); }
    }
    return message;
}