using System.Device.Gpio;
using System.Text;

namespace EdgeLed.Services
{
    public class TM1637DisplayService : ITM1637DisplayService, IDisposable
    {
        private readonly int _clockPin;
        private readonly int _dataPin;
        private readonly ILogger<TM1637DisplayService> _logger;
        private readonly GpioController _gpio;
        private readonly byte[] _digitToSegment = new byte[]
        {
            0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f, // 0-9
            0x77, 0x7c, 0x39, 0x5e, 0x79, 0x71 // A-F
        };
        private const byte ADDR_AUTO = 0x40;
        private const byte ADDR_FIXED = 0x44;
        private const byte STARTADDR = 0xc0;
        private byte _brightness = 0x8f;
        private byte[] _currentSegments = new byte[4]; // 当前段码

        public TM1637DisplayService(IConfiguration configuration, ILogger<TM1637DisplayService> logger)
        {
            // 树莓派引脚配置 (已注释)
            // _clockPin = configuration.GetValue("GPIO:ClockPin", 4);  // 树莓派默认引脚
            // _dataPin = configuration.GetValue("GPIO:DataPin", 17);   // 树莓派默认引脚
            
            // 香橙派引脚配置
            _clockPin = 69;  // PC5对应的BCM/Logical编号
            _dataPin = 72;   // PC8对应的BCM/Logical编号
            
            _logger = logger;
            _gpio = new GpioController();

            // 初始化GPIO引脚
            _gpio.OpenPin(_clockPin, PinMode.Output);
            _gpio.OpenPin(_dataPin, PinMode.Output);

            _logger.LogInformation("TM1637Display initialized with ClockPin: {ClockPin} (PC5), DataPin: {DataPin} (PC8)",
                _clockPin, _dataPin);

            Clear(); // 初始化时清空显示
        }

        public string GetCurrentDisplay()
        {
            StringBuilder sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++)
            {
                sb.Append(SegmentToChar(_currentSegments[i]));
            }
            return sb.ToString();
        }

        public byte[] GetCurrentSegments()
        {
            return _currentSegments;
        }

        public void Show(string text)
        {
            _logger.LogInformation("Displaying text: {Text}", text);
            var segments = new byte[4];
            for (int i = 0; i < Math.Min(text.Length, 4); i++)
            {
                segments[i] = CharToSegment(text[i]);
            }
            WriteSegments(segments);
        }

        public void DisplayNumber(int number)
        {
            _logger.LogInformation("Displaying number: {Number}", number);
            
            // // 检查范围：正数0-9999，负数-999到-1
            // if (number > 9999 || number < -999)
            // {
            //     _logger.LogWarning("Number {Number} out of range (-999 to 9999)", number);
            //     return;
            // }

            number = Math.Max(-999, Math.Min(9999, number));

            var segments = new byte[4] { 0, 0, 0, 0 }; // 初始化为全0
            bool isNegative = number < 0;
            number = Math.Abs(number); // 转为正数处理
            
            // 将数字转换为字符串
            string numStr = number.ToString();
            int numDigits = numStr.Length;
            
            // 如果是负数且有足够的空间显示负号
            if (isNegative && numDigits < 4)
            {
                // 右对齐，先计算起始位置
                int startPos = 4 - numDigits - 1; // 减1是为了预留负号的位置
                
                // 设置负号
                segments[startPos] = 0x40; // 负号的段码
                
                // 设置数字
                for (int i = 0; i < numDigits; i++)
                {
                    int digit = numStr[i] - '0';
                    segments[startPos + 1 + i] = _digitToSegment[digit];
                }
            }
            else
            {
                // 正数或负数但没有足够空间显示负号
                // 右对齐
                int startPos = 4 - numDigits;
                
                // 设置数字
                for (int i = 0; i < numDigits; i++)
                {
                    int digit = numStr[i] - '0';
                    segments[startPos + i] = _digitToSegment[digit];
                }
            }
            
            WriteSegments(segments);
        }

        public void ShowHex(int value)
        {
            _logger.LogInformation("Displaying hex value: {Value:X}", value);
            if (value < 0 || value > 0xFFFF)
            {
                _logger.LogWarning("Hex value {Value:X} out of range (0-FFFF)", value);
                return;
            }

            var segments = new byte[4];
            for (int i = 3; i >= 0; i--)
            {
                segments[i] = _digitToSegment[value & 0x0F];
                value >>= 4;
            }
            WriteSegments(segments);
        }

        public void ShowTime(int hours, int minutes)
        {
            _logger.LogInformation("Displaying time: {Hours:D2}:{Minutes:D2}", hours, minutes);
            if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59)
            {
                _logger.LogWarning("Invalid time values: {Hours}:{Minutes}", hours, minutes);
                return;
            }

            var segments = new byte[4];
            segments[0] = _digitToSegment[hours / 10];
            segments[1] = _digitToSegment[hours % 10];
            segments[2] = _digitToSegment[minutes / 10];
            segments[3] = _digitToSegment[minutes % 10];
            WriteSegments(segments, true);
        }

        public void DisplayTemperature(double temperature)
        {
            _logger.LogInformation("Displaying temperature: {Temperature}°C", temperature);
            int temp = (int)Math.Round(temperature * 10);
            if (temp < -999 || temp > 999)
            {
                _logger.LogWarning("Temperature {Temperature} out of range (-99.9-99.9)", temperature);
                return;
            }
            
            var segments = new byte[4];
            bool negative = temp < 0;
            temp = Math.Abs(temp);

            // 显示小数点后一位
            segments[3] = _digitToSegment[temp % 10];
            temp /= 10;
            segments[2] = (byte)(_digitToSegment[temp % 10] | 0x80); // 添加小数点
            temp /= 10;

            if (negative)
            {
                segments[1] = _digitToSegment[temp % 10];
                segments[0] = 0x40; // 负号
            }
            else
            {
                segments[1] = (byte)(temp > 0 ? _digitToSegment[temp % 10] : 0x00);
                segments[0] = temp > 9 ? _digitToSegment[temp / 10] : _digitToSegment[temp];
            }

            WriteSegments(segments);
        }

        public void Clear()
        {
            _logger.LogInformation("Clearing display");
            WriteSegments(new byte[4] { 0, 0, 0, 0 });
        }

        private char SegmentToChar(byte segment)
        {
            // 查找段码对应的字符
            for (int i = 0; i < _digitToSegment.Length; i++)
            {
                if (_digitToSegment[i] == segment)
                {
                    if (i < 10)
                        return (char)('0' + i);
                    else
                        return (char)('A' + (i - 10));
                }
            }

            // 处理特殊段码
            if (segment == 0x40) // 负号
                return '-';
            else if (segment == 0x00) // 空白
                return ' ';
                
            // 无法识别的段码
            return '?';
        }

        private void WriteSegments(byte[] segments, bool showColon = false)
        {
            // 保存当前段码
            Array.Copy(segments, _currentSegments, 4);
            
            Start();
            WriteByte(ADDR_AUTO);
            Stop();

            Start();
            WriteByte(STARTADDR);

            foreach (var segment in segments)
            {
                WriteByte(segment);
            }

            Stop();

            Start();
            WriteByte((byte)(_brightness | (showColon ? 0x80 : 0x00)));
            Stop();
        }

        private void Start()
        {
            _gpio.Write(_dataPin, PinValue.High);
            _gpio.Write(_clockPin, PinValue.High);
            _gpio.Write(_dataPin, PinValue.Low);
            _gpio.Write(_clockPin, PinValue.Low);
        }

        private void Stop()
        {
            _gpio.Write(_dataPin, PinValue.Low);
            _gpio.Write(_clockPin, PinValue.High);
            _gpio.Write(_dataPin, PinValue.High);
        }

        private void WriteByte(byte data)
        {
            for (int i = 0; i < 8; i++)
            {
                _gpio.Write(_clockPin, PinValue.Low);
                _gpio.Write(_dataPin, (data & 0x01) == 0x01 ? PinValue.High : PinValue.Low);
                data >>= 1;
                _gpio.Write(_clockPin, PinValue.High);
            }

            // 等待ACK
            _gpio.Write(_clockPin, PinValue.Low);
            _gpio.Write(_dataPin, PinValue.High);
            _gpio.Write(_clockPin, PinValue.High);
            _gpio.Write(_clockPin, PinValue.Low);
        }

        private byte CharToSegment(char c)
        {
            if (c >= '0' && c <= '9')
                return _digitToSegment[c - '0'];
            if (c >= 'A' && c <= 'F')
                return _digitToSegment[10 + (c - 'A')];
            if (c >= 'a' && c <= 'f')
                return _digitToSegment[10 + (c - 'a')];
            return 0;
        }

        public void Dispose()
        {
            try
            {
                Clear();
                _gpio.ClosePin(_clockPin);
                _gpio.ClosePin(_dataPin);
                _gpio.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing TM1637Display");
            }
        }
    }


    public interface ITM1637DisplayService
    {
        void DisplayNumber(int number);
        void DisplayTemperature(double temperature);
        void Clear();
        void Show(string text);
        void ShowHex(int value);
        void ShowTime(int hours, int minutes);
        string GetCurrentDisplay();
        byte[] GetCurrentSegments();
    }
}
