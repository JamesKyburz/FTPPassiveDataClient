using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace FTPPassiveDataClient {
  public enum TransferType : short { Ascii, Binary }
  
  public class Ftp : IDisposable
  {
    int lastResponse;
    StreamReader sessionReader, dataReader;
    StreamWriter sessionWriter;
    TcpClient session, dataSession;
    GroupCollection url;
  
    const int BLOCK_SIZE = System.UInt16.MaxValue;
  
    public Ftp(string url)
    {
      var match = Regex.Match(url, @"^(?i)ftp://(?<user>[^:]*):(?<password>[^:@]*)@(?<host>[^:]*):?(?<port>\d*)$");
      if (null == match) throw new ArgumentException("url is in format ftp://username:password@host[:port] port is optional");
      this.url = match.Groups;
    }
  
    public int TimeoutInMilliseconds { get; set; }
    public Action<string> Log { get; set; }
  
    public void RenameFile(string from, string to)
    {
      EnsureLoggedIn();
      ValidateNoWildcard(from);
      SendCommand(String.Format("RNFR {0}", from), 350, 125);
      SendCommand(String.Format("RNTO {0}", to), 250, 125);
    }
  
    bool ValidateNoWildcard(string s) { return Regex.IsMatch(s, "[*%]"); }
    void EnsureLoggedIn() { if (null == session) Login(); }
  
    public void DeleteFile(string path)
    {
      EnsureLoggedIn();
      ValidateNoWildcard(path);
      SendCommand(String.Format("DELE {0}", path), 250);
    }
  
    public void CreateDirectory(string path)
    {
      EnsureLoggedIn();
      ValidateNoWildcard(path);
      SendCommand(String.Format("MKD {0}", path), 250, 257, 550);
    }
  
    public void DeleteDirectory(string path)
    {
      EnsureLoggedIn();
      ValidateNoWildcard(path);
      SendCommand(String.Format("RMD {0}", path), 250);
    }
  
    public void SetTransferType(TransferType transferType)
    {
      EnsureLoggedIn();
      SendCommand(string.Format("TYPE {0}", transferType == TransferType.Ascii ? "A" : "I"), 200);
    }
  
    public string[] FileList(string path)
    {
      EnsureLoggedIn();
      string result = null;
      SendDataCommand(
        string.Format("NLST {0}", path),
        () => { result = ProcessResponse(true); }
      );
      ProcessResponse(226, 550);
      return Regex.Split(result, @"[\x0a\x0d]").
        Where(x => !String.IsNullOrEmpty(x)).
        ToArray();
    }
  
    public byte[] GetData(string path)
    {
      byte[] result = null;
      EnsureLoggedIn();
      SendDataCommand(
        string.Format("RETR {0}", path),
        () =>
        {
          var stream = dataReader.BaseStream;
          var bytes = new byte[BLOCK_SIZE];
          int read = 0;
          using (var ms = new MemoryStream())
          {
            while ((read = stream.Read(bytes, 0, BLOCK_SIZE)) > 0)
              ms.Write(bytes, 0, read);
            result = ms.ToArray();
          }
        }
      );
      ProcessResponse(226, 550);
      return result;
    }
  
    public void SendData(byte[] data, string path)
    {
      EnsureLoggedIn();
      SendDataCommand(
        string.Format("STOR {0}", path),
        () =>
        {
          var stream = dataSession.GetStream();
          stream.Write(data, 0, data.Length);
          stream.Flush();
        }
      );
      ProcessResponse(226, 550);
    }
  
    public void ChangeDirectory(string path)
    {
      EnsureLoggedIn();
      if (path == ".") return;
      SendCommand(String.Format("CWD {0}", path), 250);
    }
  
    string ProcessResponse(params int[] allowedReturnValues)
    {
      return ProcessResponse(false, allowedReturnValues);
    }
  
    string ProcessResponse(bool data, params int[] allowedReturnValues)
    {
      lastResponse = 0;
      bool more = false;
      var reply = new StringBuilder();
      while (true)
      {
        var line = (data ? dataReader : sessionReader).ReadLine();
        if (null == line) break;
        reply.AppendLine(line);
        if (null != Log) Log(line);
        if (Regex.IsMatch(line, @"(?-x:^.{3}\S)"))
        {
          more = true;
        }
        else if (more || Regex.IsMatch(line, @"(?-x:^(?!\x20{3}))"))
        {
          break;
        }
      }
      int.TryParse(Regex.Match(reply.ToString(), @"\d{3}").Value, out lastResponse);
      if (allowedReturnValues.Length > 0 && !allowedReturnValues.Contains(lastResponse))
        throw new IOException(string.Format("Unexpected ftp response, server said: {0}", reply));
      return reply.ToString();
    }
  
    void SendDataCommand(string command, Action dataAvailable)
    {
      PrepareDataConnection();
      SendCommand(command, 150, 125);
      dataAvailable();
      DisposeData();
    }
  
    string SendCommand(string command, params int[] allowedReturnValues)
    {
      sessionWriter.WriteLine(command);
      sessionWriter.Flush();
      return ProcessResponse(allowedReturnValues);
    }
  
    void PrepareDataConnection()
    {
      var reply = SendCommand("PASV", 227);
  
      var match = Regex.Match(reply, @"\((?:(\d+)\D*){6}\)", RegexOptions.Compiled);
  
      if (null == match) throw new IOException("Unexpected ftp response setting up data connection");
  
      var captures = match.Groups[1].Captures;
  
      int port = (int.Parse(captures[4].Value) << 8) + int.Parse(captures[5].Value);
  
      dataSession = new TcpClient();
      if (TimeoutInMilliseconds > 0)
      {
        dataSession.ReceiveTimeout = TimeoutInMilliseconds;
        dataSession.SendTimeout = TimeoutInMilliseconds;
      }
      dataSession.Connect(string.Format("{0}.{1}.{2}.{3}", captures[0].Value, captures[1].Value, captures[2].Value, captures[3].Value), port);
      dataReader = new StreamReader(dataSession.GetStream());
    }
  
    void Login()
    {
      session = new TcpClient();
      if (TimeoutInMilliseconds > 0)
      {
        session.ReceiveTimeout = TimeoutInMilliseconds;
        session.SendTimeout = TimeoutInMilliseconds;
      }
      int port = int.Parse("" == url["port"].Value ? "21" : url["port"].Value);
      session.Connect(url["host"].Value, port);
      var stream = session.GetStream();
      sessionReader = new StreamReader(stream);
      sessionWriter = new StreamWriter(stream);
      ProcessResponse(220);
      SendCommand(String.Format("USER {0}", url["user"].Value), 331, 230);
      if (lastResponse == 331)
        SendCommand(String.Format("PASS {0}", url["password"].Value), 230, 202);
    }
  
  
    public void Dispose()
    {
      try { SendCommand("QUIT"); }
      catch { };
      if (null != session) session.Close();
      session = null;
      DisposeData();
    }
  
    void DisposeData()
    {
      if (null != dataSession) dataSession.Close();
      dataSession = null;
    }
  }
}

