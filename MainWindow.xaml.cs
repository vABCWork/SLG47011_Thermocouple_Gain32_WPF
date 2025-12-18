using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Markup;
using System.Reflection;
using System.Security.Cryptography;
using ScottPlot;
using ScottPlot.Colormaps;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.IO;


namespace test_ch347
{

    // 履歴(ヒストリ)データ　クラス
    // クラス名: HistoryData
    // メンバー:  double  data0
    //            double  data1
    //            double  data2
    //            double  data3
    //            double  dt
    //

    public class HistoryData
    {
        public double data0 { get; set; }       // ch0のデータ　
        public double data1 { get; set; }       // ch1のデータ
        public double data2 { get; set; }       // ch2のデータ
        public double data3 { get; set; }       // ch3のデータ
        public double dt { get; set; }         // 日時 (double型)
    }


    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {

        // Win32 API
        // CH347DLL_EN.H での定義
        //  HANDLE WINAPI CH347OpenDevice(ULONG DevI);   
        //
        //   BOOL WINAPI CH347CloseDevice(ULONG iIndex);
        //
        //BOOL WINAPI  CH347I2C_Set(ULONG iIndex,   // Specify the device number
        //                             ULONG iMode);  // See downlink for the specified mode 
        //                                            //bit 1-bit 0: I2C interface speed /SCL frequency, 00= low speed /20KHz,01= standard /100KHz(default),10= fast /400KHz,11= high speed /750KHz
        //                                            //Other reservations, must be 0
        //
        //Process I2C data stream, 2-wire interface, clock line for SCL pin, data line for SDA pin
        //BOOL WINAPI  CH347StreamI2C(ULONG iIndex,        // Specify the device number
        //                               ULONG iWriteLength,  // The number of bytes of data to write
        //                               PVOID iWriteBuffer,  // Points to a buffer to place data ready to be written out, the first byte is usually the I2C device address and read/write direction bit
        //                               ULONG iReadLength,   // Number of bytes of data to be read
        //                               PVOID oReadBuffer); // Points to a buffer to place data ready to be read in


        // DLLのインポート
        // CH347DLL.DLLは、ドライバをインストールすると、Windowsのシステム上にコピーされる。
        //

        [DllImport("CH347DLL.DLL")]      // 32bit版 dll
        private static extern IntPtr CH347OpenDevice(UInt32 DevI);

        [DllImport("CH347DLL.DLL")]
        private static extern bool CH347CloseDevice(UInt32 iIndex);

        [DllImport("CH347DLL.DLL")]
        private static extern bool CH347I2C_Set(UInt32 iIndex, UInt32 iMode);


        [DllImport("CH347DLL.DLL")]
        private static extern bool CH347StreamI2C(UInt32 iIndex, UInt32 iWriteLength, byte[] iWriteBuffer, UInt32 iReadLength, byte[] oReadBuffer);


        [UnmanagedFunctionPointer(CallingConvention.StdCall)]

        public delegate void mPCH347_NOTIFY_ROUTINE(UInt32 iEventStatus);
        
        [DllImport("CH347DLL.DLL")]
        public static extern bool CH347SetDeviceNotify(UInt32 iIndex, string iDeviceID, mPCH347_NOTIFY_ROUTINE iNotifyRoutine);


        public const int CH347_DEVICE_REMOVE = 0;
        public const int CH347_DEVICE_REMOVE_PEND = 1;
        public const int CH347_DEVICE_ARRIVE = 3;

        public static string usb_plug = "default";

        UInt32 in_dex; // CH347のUSBポートへの接続 index (例: 0 = 最初に接続した CH347, 1= 次に接続したCH347 ) 

        string ch347_dev_id = "VID_1A86&PID_55D\0";

        mPCH347_NOTIFY_ROUTINE NOTIFY_ROUTINE;



        Byte iic_slave_adrs;      // I2Cアドレス

        Byte[] iic_rcv_data;   // IIC受信データ
        Byte[] iic_sd_data;    // IIC送信データ

        UInt32 iic_sd_num;	    // 送信データ数(スレーブアドレスを含む)
        UInt32 iic_rcv_num;     // 受信データ数

        DateTime receiveDateTime;   // 受信完了日時

    
        UInt32 i2c_clk; // 0x00=20[KHz], 0x01=100[KHz], 0x02 = 400{KHz], 0x03 = 750[KHz]


        UInt16 ch0_rd_data;       // ch0 読み出しデータ 
        UInt16 ch1_rd_data;       // ch1
        UInt16 ch2_rd_data;       // ch2
        UInt16 ch3_rd_data;       // ch3


        double ch0_data;          // 温度 熱電対 ch0 (AIN0+,AIN0-)
        double ch1_data;          // 温度 熱電対 ch1 (AIN1+,AIN1-)
        double ch2_data;          // 未使用 ch2 (AIN2+)
        double ch3_data;          // 温度 サーミスタ　ch3 (AIN3+) 

        double ch0_thermo_volt;    // 熱電対 ch0の熱起電力 [mV]
        double ch1_thermo_volt;    // 熱電対 ch1の熱起電力 [mV]
        double ch2_thermo_volt;     
        double ch3_thermo_volt;   // サーミスタ測定温度から求めた熱起電力 [mV]

        uint trend_data_item_max;             // 各リアルタイム　トレンドデータの保持数 

        double[] trend_data0;                 // トレンドデータ 0 
        double[] trend_data1;                 // トレンドデータ 1              
        double[] trend_data2;                 // トレンドデータ 2  
        double[] trend_data3;                 // トレンドデータ 3 

        double[] trend_dt;                    // トレンドデータ　収集日時

        ScottPlot.Plottables.Scatter trend_scatter_0; // トレンドデータ0  
        ScottPlot.Plottables.Scatter trend_scatter_1; // トレンドデータ1  
        ScottPlot.Plottables.Scatter trend_scatter_2; // トレンドデータ2  
        ScottPlot.Plottables.Scatter trend_scatter_3; // トレンドデータ3  


        public List<HistoryData> historyData_list;          // ヒストリデータ　データ収集時に使用


        double y_axis_top;                      // Y軸 温度目盛りの上限値
        double y_axis_bottom;                   // Y軸 温度目盛りの下限値

        public static DispatcherTimer SendIntervalTimer;  // タイマ　モニタ用　電文送信間隔   


        public MainWindow()
        {

            InitializeComponent();

            in_dex = 0;                     // CH347の使用は１つと仮定

            NOTIFY_ROUTINE = new mPCH347_NOTIFY_ROUTINE(Disp_plug_status);  // USB接続検知用、コールバック関数の作成

            bool flg_notify = CH347SetDeviceNotify(in_dex, ch347_dev_id, NOTIFY_ROUTINE);  // USB plug and unplug monitor


            IntPtr intPtr = CH347OpenDevice(in_dex);         // ドライバのハンドルを得る

            Int32 pt_val = intPtr.ToInt32();

            if (pt_val == -1)   // ハンドルが取れない場合 
            {
                Dis_connect();   // 未接続で終了
            }
            else
            {
                USB_plug_TextBox.Text = "Attached";
            }

            iic_slave_adrs = 0x08;  // SLG47011 スレーブアドレス = 0x08 
            i2c_clk = 2;            // I2Cスピード 400[KHz]  
            Boolean f_sta =  CH347I2C_Set(in_dex, i2c_clk);  // I2C通信スピード(SCL周波数)設定 

            iic_sd_data = new byte[16];     // 送信バッファ領域  
            iic_rcv_data = new byte[16];    // 受信バッファ領域

            historyData_list = new List<HistoryData>();     // モニタ時のトレンドデータ 記録用　


            SendIntervalTimer = new System.Windows.Threading.DispatcherTimer();　　// タイマーの生成(定周期モニタ用)
            SendIntervalTimer.Tick += new EventHandler(SendIntervalTimer_Tick);  // タイマーイベント
            SendIntervalTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);         // タイマーイベント発生間隔 1sec(コマンド送信周期)


            Loaded += LoadEvent;      // LoadEvent実行

        }

      
        // Windowを閉じる時の確認処理
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var result = MessageBox.Show("Close window? \r\n Data is lost.", "Question", MessageBoxButton.YesNo, MessageBoxImage.Warning);


            if (result == MessageBoxResult.No)   // Noの場合、Windowを閉じない。
            {
                e.Cancel = true;                // イベントの取り消し
            }
            else
            {
                e.Cancel = false;

                CH347CloseDevice(in_dex);       // CH347 デバイス close
            }
           
        }


        //  CH347と未接続時の処理
        private void Dis_connect()
        {
            var msg = "CH347と接続されていません。\r\n";

            MessageBox.Show(msg, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); // メッセージボックスの表示

            Close();            // アプリ終了  
        }


        //  
        //  CH347 USB plug/unplug 時の処理
        // 
        private void Disp_plug_status(UInt32 status)
        {
            if (status == CH347_DEVICE_REMOVE)    // USBケーブルが外れた
            {
                SendIntervalTimer.Stop();         // データ収集用コマンド送信タイマー停止
                
                CH347CloseDevice(in_dex);   　　　// CH347 デバイス close

                USB_plug_TextBox.Text = "Removed";
            }
            else if (status == CH347_DEVICE_ARRIVE)  // USBケーブルが接続された
            {
                USB_plug_TextBox.Text = "Attached";
                
                IntPtr intPtr = CH347OpenDevice(in_dex);    // ドライバのハンドルを得る

                SendIntervalTimer.Start();  　　　　 // 定周期　送信用タイマの開始
            }
        }


        //
        // 要素のレイアウトやレンダリングが完了し、操作を受け入れる準備が整ったときに発生
        //
        private void LoadEvent(object sender, EventArgs e)
        {
            Chart_Ini();    // チャートの初期表示
        }


        //
        // 定周期モニタでの処理
        //  
        private void SendIntervalTimer_Tick(object sender, EventArgs e)
        {
            ch0_rd_data = read_buffer_data(0x2212); // Buffer0 result 読み出し
            RcvTextBox0.Text = get_rcv_str();  　 // 受信データ表示

            ch1_rd_data = read_buffer_data(0x2224); // Buffer1 result 読み出し
            RcvTextBox1.Text = get_rcv_str();  　 // 受信データ表示

            ch2_rd_data = read_buffer_data(0x2236); // Buffer2 result 読み出し
            RcvTextBox2.Text = get_rcv_str();  　 // 受信データ表示

            ch3_rd_data = read_buffer_data(0x2248); // Buffer3 result 読み出し
            RcvTextBox3.Text = get_rcv_str();    // 受信データ表示


            Thermistor_temp_cal();              //　サーミスタ　温度計算

            Thermistor_thermo_volt();           // サーミスタ測定温度の熱起電力

            ThermoCouple_volt();               // 熱電対の熱起電力を得る  
            
            ch0_data =  ThermoCouple_Temp(ch0_thermo_volt + ch3_thermo_volt); // ch0 温度を計算
            ch1_data = ThermoCouple_Temp(ch1_thermo_volt + ch3_thermo_volt);  // ch1 温度を計算

            Ch0_TextBox.Text = ch0_data.ToString("F1");      // ch0 温度表示
            Ch0_ThermoV_TextBox.Text = ch0_thermo_volt.ToString("F2");   // ch0 熱起電力表示

            Ch1_TextBox.Text = ch1_data.ToString("F1");      // ch1 温度表示
            Ch1_ThermoV_TextBox.Text = ch1_thermo_volt.ToString("F2");   // ch1 熱起電力表示

            Store_History();                // ヒストリデータとして保持

            Chart_update();                 // チャートの更新
        }

        // 指定アドレスから 2byte読み出す。
        // 入力: 読み出しアドレス
        // 出力: 読み出したデータ
        //
        private UInt16 read_buffer_data( UInt16 adrs)
        {
         
            Byte adrs_hi = (Byte)(adrs >> 8);
            Byte adrs_lo = (Byte)adrs;

            iic_sd_data[0] = (byte)((iic_slave_adrs << 1));  // スレーブアドレスへ書き込み
            iic_sd_data[1] = adrs_hi;
            iic_sd_data[2] = adrs_lo;

            iic_sd_num = 3;     // 送信データ数 
            iic_rcv_num = 0;    // 受信データ数

            bool status = CH347StreamI2C(in_dex, iic_sd_num, iic_sd_data, iic_rcv_num, iic_rcv_data); // I2C通信

            iic_sd_data[0] = (byte)((iic_slave_adrs << 1) | 0x01);  // スレーブアドレスから読み出し
            iic_sd_num = 1;     // 送信データ数 
            iic_rcv_num = 2;    // 受信データ数

            bool status1 = CH347StreamI2C(in_dex, iic_sd_num, iic_sd_data, iic_rcv_num, iic_rcv_data); // I2C通信

            UInt16 rd_data = (UInt16)(iic_rcv_data[0] << 8);  // 上位バイト
            rd_data = (UInt16)(rd_data | (iic_rcv_data[1]));

            return rd_data;

        }

        //
        // 受信データの文字列を得る
        //
        private string get_rcv_str()
        {
            string rcv_str = "";

            for (int i = 0; i < iic_rcv_num; i++)   // 受信データ 表示用の文字列作成
            {
                rcv_str = rcv_str + iic_rcv_data[i].ToString("X2") + " ";
            }

            receiveDateTime = DateTime.Now;   // 受信完了時刻を得る

            rcv_str = rcv_str + "(" + receiveDateTime.ToString("HH:mm:ss") + ")";   // 受信データ文字列

            return rcv_str;
        }

        //
        // サーミスタの温度計算
        //  使用サーミスタ:  NCP15XH103F03RC (村田製作所)
        //                   10K[Ω](at 25℃), B = 3380
        // SimSurfing NTサーミスタ動作シュミレーターで求めた、近似式(3次)を使用.
        //
        //   T = 101.61739* x^3 - 217.83684* x^2 + 206.25610* x - 54.752738
        //  
        //   T:温度[℃], x:測定電圧[V}
        private void Thermistor_temp_cal()
        {
            double a;
            double x;

            a = (double)ch3_rd_data / 4095.0;    // ADコンバータ 12bit
           
            x = a * 1.62;       // x = 測定電圧
            Ch3_V_TextBox.Text = x.ToString("F2");  // 測定電圧の表示

            ch3_data = 101.61739 * Math.Pow(x, 3) - 217.83684 * Math.Pow(x, 2) + 206.25610 * x - 54.752738; // 温度

            Ch3_TextBox.Text = ch3_data.ToString("F1");     // 温度表示
        }

        //
        // サーミスタ測定温度から、熱起電力を得る
        //
        //  熱起電力は、熱電対 Kタイプ ( 0～1372[℃])用
        //
        //　多項式: JIS C1602 ( kikakurui.com/c1/C1602-2015-01.html )
        //
        //   E = b0 + b1*t^1 + b2*t^2 + b3*t^3 + b4*t^4 + b5*t^5 + b6*t^6 + b7*t^7 + b8*t^8 + b9*t^9 + c0*Exp(c1* (t - 126.9686)^2) 
        //   t: 温度 [℃]  
        //   E: 熱起電力 [uV]
        //
        //   b0 = -1.76004137E+01
        //   b1 = 3.89212050E+01
        //   b2 = 1.85587700E-02
        //   b3 = -9.94575929E-05
        //   b4 = 3.18409457E-07
        //   b5 = -5.60728449E-10
        //   b6 = 5.60750591E-13
        //   b7 = -3.20207200E-16
        //   b8 = 9.71511472E-20
        //   b9 = -1.21047213E-23
        //
        //   c0 = 1.185976E+02
        //   c1 = -1.183432E-04
        //
        private void Thermistor_thermo_volt()
        {
            double t;
            double E;
            double[] b_const;

            b_const = new double[] { -1.76004137E+01, 3.89212050E+01, 1.85587700E-02, -9.94575929E-05, 3.18409457E-07,
                                     -5.60728449E-10, 5.60750591E-13, -3.20207200E-16, 9.71511472E-20,-1.21047213E-23};

            double c0 = 1.185976E+02;
            double c1 = -1.183432E-04;


            t = ch3_data;           // サーミスタ測定温度

            E = 0;

            for (int i = 0; i < 10; i++)
            {
                E = E + b_const[i]* Math.Pow(t, i);
            }

            double a = c1 * Math.Pow(t-126.9686, 2);

            E = E + c0* Math.Exp(a);

            ch3_thermo_volt = E * 0.001;   // [mV]

            Ch3_Thermo_Volt_TextBox.Text = ch3_thermo_volt.ToString("F3");  //　熱起電力の表示
        }

        //
        // 熱電対の熱起電力を得る
        // A/Dコンバータ 12bit
        // Gain = 32
        // Vref = 1.62[V], Vref/2 = 0.81[V]
        // 0.81[V]/32 = 0.0253125 [V]
        //
        // A/Dコンバータ 12bitの場合:
        // AD値             測定電圧       温度
        //    0              -25.3 [mV]
        //  2047 (0x7ff)        0 [mV]     0[℃]
        //  4095 (0xfff)     25.3 [mV]　 610[℃]　
        //
        // A/Dコンバータ 14bitの場合:
        // AD値             測定電圧       温度
        //    0              -12.65 [mV]
        //  8192 (0x2000)        0 [mV]     0[℃]
        // 16383 (0x3fff)     12.65 [mV]　 310[℃]　
        //
        // 
        private void ThermoCouple_volt()
        {
            ch0_thermo_volt = ( (double)(ch0_rd_data - 2047) / 2047 ) * 25.3125;   // [mV]

            ch1_thermo_volt = ((double)(ch1_rd_data - 2047) / 2047) * 25.3125;

        }

        //  熱起電力から温度を得る
        //   入力: 熱起電力[mV] 
        //   出力: 温度[℃]    
        // 多項式:
        //   T = c0 + c1*E^1 + c2*E^2 + c3*E^3 + c4*E^4 + c5*E^5 + c6*E^6 + c7*E^7 + c8*E^8 + c9*E^9 
        //   T: 温度 [℃]  
        //   E: 熱起電力 [uV]
        //
        //   c0 = 0.0 
        //   c1 = 2.5083550E-02
        //   c2 = 7.8601060E-08
        //   c3 = -2.5031310E-10
        //   c4 = 8.3152700E-14
        //   c5 = -1.2280340E-17
        //   c6 = 9.8040360E-22
        //   c7 =-4.4130300E-26
        //   c8 =  1.0577340E-30
        //   c9 = -1.0527550E-35
        //
        private double  ThermoCouple_Temp( double e_mv) 
        {
            double t;
            double e;

            double[] c_const;

            c_const = new double[] {   0.0, 2.508355E-02, 7.860106E-08, -2.503131E-10, 8.315270E-14,
                                      -1.228034E-17, 9.804036E-22,-4.413030E-26, 1.057734E-30, -1.052755E-35};
            t = 0;
            e = e_mv  * 1000;    // 熱起電力[uV]

            for (int i = 0; i < 10; i++)
            {
                t = t + c_const[i] * Math.Pow(e, i);
            }

            return t;
        }


        //
        //  ヒストリデータとして保持
        //
        private void Store_History()
        {

            HistoryData historyData = new HistoryData();     // 保存用ヒストリデータ

            historyData.data0 = ch0_data;
            historyData.data1 = ch1_data;
            historyData.data2 = ch2_data;
            historyData.data3 = ch3_data; 

            historyData.dt = receiveDateTime.ToOADate();   // 受信日時を deouble型で格納

            historyData_list.Add(historyData);          // Listへ保持

        }


     
        //
        //   チャートの更新
        private void Chart_update()
        {

            // 1スキャン前のデータを移動後、最新のデータを入れる
            Array.Copy(trend_data0, 1, trend_data0, 0, trend_data_item_max - 1);
            trend_data0[trend_data_item_max - 1] = ch0_data;

            Array.Copy(trend_data1, 1, trend_data1, 0, trend_data_item_max - 1);
            trend_data1[trend_data_item_max - 1] = ch1_data;

            Array.Copy(trend_data2, 1, trend_data2, 0, trend_data_item_max - 1);
            trend_data2[trend_data_item_max - 1] = ch2_data;

            Array.Copy(trend_data3, 1, trend_data3, 0, trend_data_item_max - 1);
            trend_data3[trend_data_item_max - 1] = ch3_data;


            Array.Copy(trend_dt, 1, trend_dt, 0, trend_data_item_max - 1);
            trend_dt[trend_data_item_max - 1] = receiveDateTime.ToOADate();    // 受信日時 double型に変換して、格納


            Axis_make();            // 軸の作成

            wpfPlot_Trend.Refresh();   // リアルタイム グラフの更新


        }



        //
        // 　チャートの初期化(リアルタイム　チャート用)
        //
        private void Chart_Ini()
        {
            trend_data_item_max = 30;             // 各リアルタイム　トレンドデータの保持数(=30 ) 1秒毎に収集すると、30秒分のデータ

            trend_data0 = new double[trend_data_item_max];
            trend_data1 = new double[trend_data_item_max];
            trend_data2 = new double[trend_data_item_max];
            trend_data3 = new double[trend_data_item_max];

            trend_dt = new double[trend_data_item_max];

            DateTime datetime = DateTime.Now;   // 現在の日時

            DateTime[] myDates = new DateTime[trend_data_item_max];  // 日時型



            for (int i = 0; i < trend_data_item_max; i++)  // 初期値の設定
            {
                trend_data0[i] = 30 + i;
                trend_data1[i] = 20 + i;
                trend_data2[i] = 10 + i;
                trend_data3[i] = 0 + i;

                myDates[i] = datetime + new TimeSpan(0, 0, i);  // i秒増やす

                trend_dt[i] = myDates[i].ToOADate();   // (現在の日時 + i 秒)をdouble型に変換
            }


            trend_scatter_0 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data0, ScottPlot.Colors.Blue); // プロット plot the data array only once
            trend_scatter_1 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data1, ScottPlot.Colors.Orange);
            trend_scatter_2 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data2, ScottPlot.Colors.Gainsboro);
            trend_scatter_3 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data3, ScottPlot.Colors.Green);


            wpfPlot_Trend.UserInputProcessor.IsEnabled = false;     // マウスによるパン(グラフの移動)、ズーム(グラフの拡大、縮小)の操作禁止

            Axis_make();            // 軸の作成

            // 凡例の表示
            // 参考:scottplot.net/cookbook/5.0/Legend/
            //
            wpfPlot_Trend.Plot.Legend.FontSize = 24;

            trend_scatter_0.LegendText = "ch0";
            trend_scatter_1.LegendText = "ch1";
            trend_scatter_2.LegendText = "ch2";
            trend_scatter_3.LegendText = "ch3";

            wpfPlot_Trend.Plot.ShowLegend(Alignment.UpperRight, ScottPlot.Orientation.Vertical);


            wpfPlot_Trend.Refresh();        // データ変更後のリフレッシュ


        }


        //
        // 軸の作成
        //
        private void Axis_make()
        {
            y_axis_top = 250;                       // Y軸　上限値
            y_axis_bottom = 0;                      // Y軸　下限値

            // X軸の日時リミットを、最終日時+1秒にする
            DateTime dt_end = DateTime.FromOADate(trend_dt[trend_data_item_max - 1]); // double型を　DateTime型に変換
            TimeSpan dt_sec = new TimeSpan(0, 0, 1);    // 1 秒
            DateTime dt_limit = dt_end + dt_sec;      // DateTime型(最終日時+ 1秒) 
            double dt_ax_limt = dt_limit.ToOADate();   // double型(最終日時+ 1秒) 


            wpfPlot_Trend.Plot.Axes.SetLimits(trend_dt[0], dt_ax_limt, 0, 250);  // X軸の最小=現在の時間 ,X軸の最大=最終日時+1秒,Y軸下限=0[℃], Y軸上限=250 [℃]

            custom_ticks();                             // X軸の目盛りのカスタマイズ

            //wpfPlot_Trend.Plot.Axes.Left.Label.FontSize = 24;                 // Y軸   ラベルのフォントサイズ変更  :
            //wpfPlot_Trend.Plot.Axes.Left.Label.Text = "[C] celsius";          // Y軸のラベル (scottplot.net/cookbook/5.0/Styling/AxisCustom/)

        }

        //
        //  目盛りのカスタマイズ 
        // 参考: scottplot.net/cookbook/5.0/CustomizingTicks/
        //
        //       Custom Tick DateTimes
        // Users may define custom ticks using DateTime units
        // 
        private void custom_ticks()
        {
            DateTime dt;
            string label;

            // create a manual DateTime tick generator and add ticks
            ScottPlot.TickGenerators.DateTimeManual ticks = new ScottPlot.TickGenerators.DateTimeManual();

            //for (int i = 0; i < trend_data_item_max; i++)  // 1秒毎に目盛りのラベル表示
            //{
            //    DateTime dt = DateTime.FromOADate(trend_dt[i]);
            //    string label = dt.ToString("HH:mm:ss");
            //    ticks.AddMajor(dt, label);
            //}

           
            dt = DateTime.FromOADate(trend_dt[1]);  // 先頭 + 1の時刻　目盛りのラベル表示
            label = dt.ToString("HH:mm:ss");
            ticks.AddMajor(dt, label);

            UInt16 t = (ushort)(trend_data_item_max / 2); 
            dt = DateTime.FromOADate(trend_dt[t]);  // 中間の時刻　目盛りのラベル表示
            label = dt.ToString("HH:mm:ss");
            ticks.AddMajor(dt, label);

            dt = DateTime.FromOADate(trend_dt[trend_data_item_max - 1]);  // 最後の時刻　目盛りのラベル表示
            label = dt.ToString("HH:mm:ss");
            ticks.AddMajor(dt, label);

            wpfPlot_Trend.Plot.Axes.Bottom.TickGenerator = ticks;    　　　　// tell the horizontal axis to use the custom tick generator

            wpfPlot_Trend.Plot.Axes.Bottom.TickLabelStyle.FontSize = 24;      //  X軸　目盛りのフォントサイズ


            wpfPlot_Trend.Plot.Axes.Left.TickLabelStyle.FontSize = 24;        //  Y軸　目盛りのフォントサイズ
        }




        // モニタ開始ボタン
        private void Start_Monitor_Button_Click(object sender, RoutedEventArgs e)
        {
            SendIntervalTimer.Start();   // 定周期　送信用タイマの開始
        }

        // モニタ停止ボタン
        private void Stop_Monitor_Button_Click(object sender, RoutedEventArgs e)
        {
            SendIntervalTimer.Stop();     // データ収集用コマンド送信タイマー停止
        }



        // チェックボックスによるトレンド線の表示 
        private void CH_N_Show(object sender, RoutedEventArgs e)
        {

            if (trend_scatter_0 is null) return;
            if (trend_scatter_1 is null) return;
            if (trend_scatter_2 is null) return;
            if (trend_scatter_3 is null) return;

            CheckBox checkBox = (CheckBox)sender;

            if (checkBox.Name == "Ch0_CheckBox")
            {
                trend_scatter_0.IsVisible = true;
            }
            else if (checkBox.Name == "Ch1_CheckBox")
            {
                trend_scatter_1.IsVisible = true;
            }
            else if (checkBox.Name == "Ch2_CheckBox")
            {
                trend_scatter_2.IsVisible = true;
            }
            else if (checkBox.Name == "Ch3_CheckBox")
            {
                trend_scatter_3.IsVisible = true;
            }


            wpfPlot_Trend.Refresh();   // グラフの更新

        }

        // チェックボックスによるトレンド線の非表示
        private void CH_N_Hide(object sender, RoutedEventArgs e)
        {
            if (trend_scatter_0 is null) return;
            if (trend_scatter_1 is null) return;
            if (trend_scatter_2 is null) return;
            if (trend_scatter_3 is null) return;

            CheckBox checkBox = (CheckBox)sender;

            if (checkBox.Name == "Ch0_CheckBox")
            {
                trend_scatter_0.IsVisible = false;
            }
            else if (checkBox.Name == "Ch1_CheckBox")
            {
                trend_scatter_1.IsVisible = false;
            }
            else if (checkBox.Name == "Ch2_CheckBox")
            {
                trend_scatter_2.IsVisible = false;
            }
            else if (checkBox.Name == "Ch3_CheckBox")
            {
                trend_scatter_3.IsVisible = false;
            }

            wpfPlot_Trend.Refresh();   // グラフの更新
        }

    

        // 保持しているデータをファイルへ保存
        private void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            string path;

            string str_one_line;

            SaveFileDialog sfd = new SaveFileDialog();           //　SaveFileDialogクラスのインスタンスを作成 

            sfd.FileName = "temp_trend.csv";                              //「ファイル名」で表示される文字列を指定する

            sfd.Title = "保存先のファイルを選択してください。";        //タイトルを設定する 

            sfd.RestoreDirectory = true;                 //ダイアログボックスを閉じる前に現在のディレクトリを復元するようにする

            if (sfd.ShowDialog() == true)            //ダイアログを表示する
            {
                path = sfd.FileName;

                try
                {
                    System.IO.StreamWriter sw = new System.IO.StreamWriter(path, false, System.Text.Encoding.Default);

                    str_one_line = DataMemoTextBox.Text; // メモ欄
                    sw.WriteLine(str_one_line);         // 1行保存


                    str_one_line = "DateTime" + "," + "ch0[℃]" + "," + "ch1[℃]" + "," + "ch2[℃]" + "," + "ch3[℃]";
                    sw.WriteLine(str_one_line);         // 1行保存


                    foreach (HistoryData historyData in historyData_list)         // historyData_listの内容を保存
                    {
                        DateTime dateTime = DateTime.FromOADate(historyData.dt); // 記録されている日時(double型)を　DateTime型に変換

                        string st_dateTime = dateTime.ToString("yyyy/MM/dd HH:mm:ss.fff");             // DateTime型を文字型に変換　（2021/10/22 11:09:06.125 )

                        string st_dt0 = historyData.data0.ToString("F1");       //データ(ch0) 文字型に変換 (25.0)
                        string st_dt1 = historyData.data1.ToString("F1");       // 
                        string st_dt2 = historyData.data2.ToString("F1");       // 
                        string st_dt3 = historyData.data3.ToString("F1");       // 


                        str_one_line = st_dateTime + "," + st_dt0 + "," + st_dt1 + "," + st_dt2 + "," + st_dt3;

                        sw.WriteLine(str_one_line);         // 1行保存
                    }

                    sw.Close();
                }

                catch (System.Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }
        }


        // 収集済みのデータをクリアの確認
        private void Clear_Button_Click(object sender, RoutedEventArgs e)
        {
            string messageBoxText = "収集済みのデータがクリアされます。";
            string caption = "Check clear";

            MessageBoxButton button = MessageBoxButton.YesNoCancel;
            MessageBoxImage icon = MessageBoxImage.Warning;
            MessageBoxResult result;

            result = MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);

            switch (result)
            {
                case MessageBoxResult.Yes:      // Yesを押した場合
                    historyData_list.Clear();   // 収集済みのデータのクリア
                    break;

                case MessageBoxResult.No:
                    break;

                case MessageBoxResult.Cancel:
                    break;
            }
        }

        // トレンド 履歴画面
        private void History_Button_Click(object sender, RoutedEventArgs e)
        {

            var window = new HistoryWindow();      // 注意メッセージのダイアログを開く
            window.Owner = this;
            window.Show();
        }

    }

}
