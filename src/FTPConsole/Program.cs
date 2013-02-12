using System;
using FTPPassiveDataClient;

namespace FtpConsole
{
  class Program
  {
    static void Main(string[] args)
    {
      using (var ftp = new Ftp("ftp://username:password@madeup.com")) {
        ftp.TimeoutInMilliseconds = 5000;
        ftp.SetTransferType(TransferType.Binary);
        ftp.Log = (s) => Console.WriteLine(s);
        ftp.ChangeDirectory("jchk_test");
        foreach (var file in ftp.FileList("*.png")) {
          Console.WriteLine("File={0}", file);
        }
        System.IO.File.WriteAllBytes(@"C:\ftptest.png", ftp.GetData("o.png"));
        ftp.CreateDirectory("test");
        ftp.ChangeDirectory("test");
        ftp.SendData(new byte[] { 65, 65, 65 }, "o.txt");
        ftp.DeleteFile("o.txt");
        ftp.ChangeDirectory("..");
        ftp.DeleteFile("o.png");
        ftp.DeleteDirectory("test");
      }
    }
  }
}
