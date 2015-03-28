﻿namespace Serial
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;

    using LabNation.Interfaces;

    /// <summary>
    /// The serial.
    /// </summary>
    [Export(typeof(IDecoder))]
    public class Serial : IDecoder
    {
        /// <summary>
        /// Gets the description.
        /// </summary>
        public DecoderDescription Description
        {
            get
            {
                return new DecoderDescription
                           {
                               Name = "Serial decoder",
                               ShortName = "UART",
                               Author = "robert44",
                               VersionMajor = 0,
                               VersionMinor = 1,
                               Description = "Serial decoder",
                               InputWaveformTypes = new Dictionary<string, Type> { { "UART", typeof(float) } },
                               Parameters = new DecoderParameter[]
                                       {
                                           // new DecoderParamaterInts("Baudrate", new[] { 0, 75, 110, 300, 1200, 2400, 4800, 9600, 14400, 19200, 28800, 38400, 57600, 115200 }, "bits per second", 0, "Bits per second (baudrate)."),
                                           new DecoderParamaterInts("Databits", new[] { 7, 8 }, "Databits", 8, "Data bits."),
                                           new DecoderParamaterStrings("Parity", new[] { "None", "Odd", "Even", "Mark", "Space" }, "None", "Parity."),
                                           new DecoderParamaterInts("Stopbits", new[] { 1, 2 }, "Stopbits", 1, "stop bit setting."),
                                           new DecoderParamaterStrings("Mode", new[] { "UART", "RS232" }, "RS232", "Select if the signal needs to be inverted.")
                                       }
                           };
            }
        }

        /// <summary>
        /// The decoding method.
        /// </summary>
        /// <param name="inputWaveforms"> The input waveforms. </param>
        /// <param name="parameters"> The parameters. </param>
        /// <param name="samplePeriod"> The sample period. </param>
        /// <returns> The output returned to the scope. </returns>
        public DecoderOutput[] Decode(Dictionary<string, Array> inputWaveforms, Dictionary<string, object> parameters, double samplePeriod)
        {
            var decoderOutputList = new List<DecoderOutput>();

            try
            {
                //// Get samples.
                var serialData = (float[])inputWaveforms["UART"];

                //// Fetch parameters.
                //// var selectedBaudrate = (int)parameters["Baudrate"];
                var selectedDatabits = (int)parameters["Databits"];
                var selectedStopbits = (int)parameters["Stopbits"];
                var selectedMode = (string)parameters["Mode"];
                bool inverted = selectedMode == "UART";

                int parityLength;
                Parity parity;
                switch ((string)parameters["Parity"])
                {
                    case "Odd":
                        parity = Parity.Odd;
                        parityLength = 1;
                        break;
                    case "Even":
                        parity = Parity.Even;
                        parityLength = 1;
                        break;
                    case "Mark":
                        parity = Parity.Mark;
                        parityLength = 1;
                        break;
                    case "Space":
                        parity = Parity.Space;
                        parityLength = 1;
                        break;
                    default:
                        parity = Parity.None;
                        parityLength = 0;
                        break;
                }

                int frameLength = 1 + selectedDatabits + parityLength + selectedStopbits;

                //// Sort values.
                var dic = new Dictionary<float, int>();
                foreach (var f in serialData)
                {
                    if (dic.ContainsKey(f))
                    {
                        dic[f]++;
                    }
                    else
                    {
                        dic.Add(f, 1);
                    }
                }

                float min = float.MaxValue;
                float max = float.MinValue;

                //// Take Min Max value.
                foreach (var i in dic.Where(i => i.Value > 50))
                {
                    if (i.Key < min)
                    {
                        min = i.Key;
                    }

                    if (i.Key > max)
                    {
                        max = i.Key;
                    }
                }

                if (Math.Abs(min - float.MaxValue) < 0.01 || Math.Abs(max - float.MinValue) < 0.01)
                {
                    return decoderOutputList.ToArray();
                }

                //// Calc Low High threshold, this way we can handle almost all signal levels.
                var th = (max - min) / 4;
                float maxth = max - th;
                float minth = min + th;

                int indexSignalUp = -1;
                int indexSignalDown = -1;
                var bits = new List<Bit>();

                //// Get bit length.
                for (int i = 0; i < serialData.Length - 1; i++)
                {
                    if (serialData[i] > maxth)
                    {
                        if (indexSignalDown == -1)
                        {
                            indexSignalDown = i;
                        }

                        if (indexSignalUp != -1)
                        {
                            double bitlength = Math.Abs(indexSignalDown - indexSignalUp) * samplePeriod * 1000;
                            indexSignalUp = -1;
                            bits.Add(inverted ? new Bit(i, bitlength, 0) : new Bit(i, bitlength, 1));
                            //// Debug.WriteLine("bitlength L-H = {0} , index = {1}", bitlength, i);
                        }
                    }

                    if (serialData[i] < minth)
                    {
                        if (indexSignalUp == -1)
                        {
                            indexSignalUp = i;
                        }

                        if (indexSignalDown != -1)
                        {
                            double bitlength = Math.Abs(indexSignalDown - indexSignalUp) * samplePeriod * 1000;
                            indexSignalDown = -1;
                            bits.Add(inverted ? new Bit(i, bitlength, 1) : new Bit(i, bitlength, 0));
                            //// Debug.WriteLine("bitlength H-L = {0}, index = {1}", bitlength, i);
                        }
                    }
                }

                //// Minimum bit length in msec.
                double minimumBitlength = bits.Select(bit => bit.Length).Concat(new[] { double.MaxValue }).Min();
                //// Debug.WriteLine("Minimum bit length = {0} msec", minimumBitlength);

                if (Math.Abs(minimumBitlength - double.MaxValue) < 0.1)
                {
                    // possible show error in detecting baudrate.
                    return decoderOutputList.ToArray();
                }

                var resultBits = new List<Bit>();
                int indexstep = (int)(minimumBitlength / (1000.0 * samplePeriod));
                var bitstring = new StringBuilder();

                foreach (Bit bit in bits)
                {
                    var count = (int)(bit.Length / minimumBitlength);
                    if (count < (1 + selectedDatabits + 1))
                    {
                        for (var i = 0; i < count; i++)
                        {
                            resultBits.Add(new Bit(bit.Index + (i * indexstep), indexstep, bit.Value));
                            bitstring.Append(bit.Value == 1 ? "1" : "0");
                        }
                    }
                }

                resultBits.Add(new Bit(bits[bits.Count - 1].Index + indexstep, indexstep, 1));
                var bitstr = bitstring.ToString();
                var bitstream = bitstr.Substring(bitstr.IndexOf('0')) + "1"; // Add end bit

                //// Debug.WriteLine(bitstream);
                for (int i = 0; i < bitstream.Length; i += frameLength)
                {
                    if (i + selectedDatabits + parityLength + selectedStopbits < bitstream.Length)
                    {
                        // Find start and stop bit.
                        if (bitstream[i] == '0' && bitstream[i + selectedDatabits + parityLength + selectedStopbits] == '1')
                        {
                            if ((selectedStopbits == 1) | ((selectedStopbits == 2) && bitstream[i + selectedDatabits + parityLength + 1] == '1'))
                            {
                                var databitsStr = bitstream.Substring(i + 1, selectedDatabits);

                                bool parityOk = true;
                                if (parity != Parity.None)
                                {
                                    int oneCount;
                                    char parityBit = bitstream[i + selectedDatabits + 1];
                                    switch (parity)
                                    {
                                        case Parity.Odd:
                                            oneCount = databitsStr.Count(x => x == '1');
                                            if (parityBit == '1')
                                            {
                                                oneCount++;
                                            }

                                            if (oneCount % 2 == 0)
                                            {
                                                parityOk = false;
                                            }

                                            break;
                                        case Parity.Even:
                                            oneCount = databitsStr.Count(x => x == '1');
                                            if (parityBit == '1')
                                            {
                                                oneCount++;
                                            }

                                            if (oneCount % 2 == 1)
                                            {
                                                parityOk = false;
                                            }

                                            break;
                                        case Parity.Mark:
                                            if (parityBit != '1')
                                            {
                                                parityOk = false;
                                            }

                                            break;
                                        case Parity.Space:
                                            if (parityBit != '0')
                                            {
                                                parityOk = false;
                                            }

                                            break;
                                    }
                                }

                                if (parityOk)
                                {
                                    var databits = databitsStr.ToCharArray();
                                    Array.Reverse(databits);
                                    byte data = Convert.ToByte(new string(databits), 2);
                                    decoderOutputList.Add(new DecoderOutputValue<byte>(resultBits[i].Index, resultBits[i + selectedDatabits + selectedStopbits].Index, DecoderOutputColor.Red, data, string.Empty));
                                }
                            }
                        }
                    }
                }

                double baudrate = 1.0 / (minimumBitlength / 1000.0);
                Debug.WriteLine("Detected: {0} baud.", (int)baudrate);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            return decoderOutputList.ToArray();
        }

        /// <summary>
        /// The parity.
        /// </summary>
        private enum Parity
        {
            None,
            Odd,
            Even,
            Mark,
            Space
        }

        /// <summary>
        /// The bit.
        /// </summary>
        private class Bit
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Bit"/> class.
            /// </summary>
            /// <param name="index"> The index. </param>
            /// <param name="length"> The length. </param>
            /// <param name="val"> The val. </param>
            public Bit(int index, double length, int val)
            {
                this.Index = index;
                this.Value = val;
                this.Length = length;
            }

            /// <summary>
            /// Gets the index.
            /// </summary>
            public int Index { get; private set; }

            /// <summary>
            /// Gets the value.
            /// </summary>
            public int Value { get; private set; }

            /// <summary>
            /// Gets the length.
            /// </summary>
            public double Length { get; private set; }
        }
    }
}