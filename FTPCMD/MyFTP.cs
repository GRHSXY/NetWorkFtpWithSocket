﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FTPCMD
{

    class MyFTP
    {
        //连接相关常量
        private string CRLF = "\r\n";
        private int CmdPort = 21;
        private int BufferSize = 1024;
        private int ByteArraySize = 1030;
        //连接相关参数
        private string FtpIP;
        private int DataPort;
        public string UserName;
        public string PassWord;
        //命令套接字与数据套接字
        private Socket cmdSocket;
        private Socket dataSocket;

        /// <summary>
        /// 获取FTP服务器控制端口返回的消息
        /// </summary>
        private string GetCmdMessage()
        {
            byte[] data = new byte[BufferSize];//这里传递一个byte数组，实际上这个data数组用来接收数据
            int length = cmdSocket.Receive(data); //length返回值表示接收了多少字节的数据
            string message = Encoding.UTF8.GetString(data, 0, length); //把接收到的数据做一个转化
            return message;
        }
        /// <summary>
        /// 连接上IP对应的FTP服务器
        /// </summary>
        public string Connect(string IP)
        {
            FtpIP = IP;
            cmdSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            cmdSocket.Connect(IP, CmdPort);
            return GetCmdMessage();
        }

        /// <summary>
        /// 使用用户名和密码登录FTP服务器
        /// </summary>
        public string LoginIn(string username, string password)
        {
            //先传送用户名
            UserName = username;
            string UserCmd = "USER " + UserName + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(UserCmd));
            GetCmdMessage();//获取一个无用的信息
            //再传送密码
            PassWord = password;
            string PswCmd = "PASS " + PassWord + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(PswCmd));

            return GetCmdMessage();//返回是否登录成功的字符串
        }
        /// <summary>
        /// 使FTP服务器进入被动模式 返回数据端口号
        /// </summary>
        public string PassiveMode()
        {
            //发送被动模式命令
            string PassiveCmd = "PASV" + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(PassiveCmd));
            string message = GetCmdMessage();
            //处理返回的数据端口
            string retstr;
            string[] retArray = Regex.Split(message, ",");
            if (retArray[5][2] != ')') 
                retstr = retArray[5].Substring(0, 3);
            else 
                retstr = retArray[5].Substring(0, 2);
            DataPort = Convert.ToInt32(retArray[4]) * 256 + Convert.ToInt32(retstr);
            //将数据套接字绑定数据端口
            dataSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            dataSocket.Connect(FtpIP, DataPort);

            return DataPort.ToString();
        }

        /// <summary>
        /// 从FTP服务器下载文件
        /// </summary>
        public string DownLoadFile(string fileName,int breakPoint = 0)//便于测试，加入断点，到断点则停止下载
        {
            //进入被动模式
            PassiveMode();
            //申请下载命令
            string downCmd = "RETR " + fileName + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(downCmd));
            //创建文件
            FileStream fstrm = new FileStream(fileName, FileMode.OpenOrCreate);
            byte[] fbytes = new byte[ByteArraySize];

            int sum = 0;//读取的总字节数
            int num;//每次读取的字节数
            //从数据端口读取数据
            while ((num = dataSocket.Receive(fbytes)) != 0)
            {
                if (sum + num > breakPoint && breakPoint > 0) //如果此次读取的字节超过中断点则中断
                {
                    fstrm.Write(fbytes, 0, breakPoint - sum);
                    dataSocket.Close();//传输成功后关闭数据套接字
                    GetCmdMessage();
                    return "BreakPoint:" + breakPoint.ToString();
                }
                else
                {
                    fstrm.Write(fbytes, 0, num);
                    sum += num;
                }
            }
            fstrm.Close();

            dataSocket.Close();//传输成功后关闭数据套接字

            return GetCmdMessage();
        }
        /// <summary>
        /// 断点续传:下载
        /// </summary>
        public string DownLoadFileFromBreakPoint(string fileName,int breakPoint)
        {
            //进入被动模式
            PassiveMode();
            //申请断点续传
            string breakPointCMD = "REST " + breakPoint.ToString() + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(breakPointCMD));
            GetCmdMessage();
            //申请下载命令
            string downCmd = "RETR " + fileName + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(downCmd));
            GetCmdMessage();
            //写入文件
            FileStream fstrm = new FileStream(fileName, FileMode.Open);
            byte[] fbytes = new byte[ByteArraySize];
            fstrm.Seek(breakPoint,SeekOrigin.Begin);
            int num;
            while ((num = dataSocket.Receive(fbytes)) != 0)
            {
                fstrm.Write(fbytes, 0, num);
            }
            return "Finished";
        }
        /// <summary>
        /// 向FTP服务器上传文件
        /// </summary>
        public string UpLoadFile(string fileName,int breakPoint = 0)//便于测试，加入断点，到断点则停止上传
        {
            //进入被动模式
            PassiveMode();
            string uplodeCMD = "STOR " + fileName + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(uplodeCMD));
            //打开文件读取数据
            FileStream fstrm = new FileStream(fileName, FileMode.Open);
            byte[] fbytes = new byte[BufferSize];
            int sum = 0;//计算总共读取了多少文件字节
            int num;//计算一次读取了多少文件字节
            while ((num = fstrm.Read(fbytes, 0, BufferSize)) != 0)//没到文件末尾则循环
            {
                if (sum + num > breakPoint && breakPoint != 0)//如果此次读取的字节超过中断点则中断
                {
                    byte[] endBytes = new byte[breakPoint-sum];
                    Array.Copy(fbytes, 0, endBytes, 0, breakPoint - sum);
                    dataSocket.Send(endBytes);
                    dataSocket.Close();//传输成功后关闭数据套接字
                    GetCmdMessage();
                    return "BreakPoint:" + breakPoint.ToString();
                }
                else
                {
                    if (num == BufferSize)//当数组满直接发送
                        dataSocket.Send(fbytes);
                    else//数组不满则只发送前面一部分
                    {
                        byte[] endBytes = new byte[num];
                        Array.Copy(fbytes, 0, endBytes, 0, num);
                        dataSocket.Send(endBytes);
                        break;
                    }

                    sum += num;
                }
            }
            fstrm.Close();
            return GetCmdMessage();
        }

        /// <summary>
        /// 断点续传：上传
        /// </summary>
        public string UpLoadFileFromBreakPoint(string fileName, int breakPoint)
        {
            //进入被动模式
            PassiveMode();
            //申请断点续传
            string breakPointCMD = "REST " + breakPoint.ToString() + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(breakPointCMD));
            GetCmdMessage();
            //申请上传命令
            string uplodeCMD = "STOR " + fileName + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(uplodeCMD));
            GetCmdMessage();
            //从断点打开文件读取数据
            FileStream fstrm = new FileStream(fileName, FileMode.Open);
            fstrm.Seek(breakPoint, SeekOrigin.Begin);//寻找偏移

            byte[] fbytes = new byte[BufferSize];
            int cnt ;//计算读取了多少文件字节
            while ((cnt = fstrm.Read(fbytes, 0, 1024)) != 0)//没到文件末尾则循环
            {
                if (cnt == BufferSize)//当数组满直接发送
                    dataSocket.Send(fbytes);
                else//数组不满则只发送前面一部分
                {
                    byte[] endBytes = new byte[cnt];
                    Array.Copy(fbytes, 0, endBytes, 0, cnt);
                    dataSocket.Send(endBytes);
                    break;
                }
            }
            fstrm.Close();
            return "Finished";
        }
        /// <summary>
        /// 关闭数据套接字
        /// </summary>
        public string CloseDataSocket()
        {
            dataSocket.Close();
            return GetCmdMessage();
        }
        /// <summary>
        /// 关闭连接
        /// </summary>
        public string Close()
        {
            string quitCMD = "QUIT" + CRLF;
            cmdSocket.Send(Encoding.UTF8.GetBytes(quitCMD));
            string message = GetCmdMessage();
            cmdSocket.Close();
            return message;
        }
    }
}