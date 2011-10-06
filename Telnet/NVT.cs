using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Telnetd
{
    class NVT : Stream
    {
        private int _row;
        private int _col;
        private int _total;
        private List<List<byte>> _lines;

        public NVT()
        {
            _row = 0;
            _col = 0;
            _total = 0;
            _lines = new List<List<byte>>();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (0 < _lines.Count)
            {
                List<byte> line = _lines[0];
                while (0 < line.Count)
                {
                    if (read < count)
                    {
                        buffer[offset++] = line[0];
                        line.RemoveAt(0);
                        _col--;
                        read++;
                    }
                    else
                    {
                        return read;
                    }
                }
                if (0 < _row)
                {
                    foreach (byte b in Environment.NewLine)
                    {
                        buffer[offset++] = b;
                        read++;
                    }
                    _lines.RemoveAt(0);
                    _row--;
                }
            }
            return read;
        }

        public override long Seek(long l, SeekOrigin so)
        {
            throw new NotImplementedException();

        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = offset, j = 0; j < count; i++, j++)
            {
                byte b = buffer[i];
                if ('\b' == b && 0 < _col)
                {
                    _col--;
                }
                else if ('\n' == b)
                {
                    _row++;
                }
                else if ('\r' == b)
                {
                    _col = 0;
                }
                else
                {
                    while (_row + 1 > _lines.Count)
                    {
                        _lines.Add(new List<byte>());
                    }
                    List<byte> line = _lines[_row];
                    if (_col + 1 > line.Count)
                    {
                        line.Add(b);
                        _total++;
                    }
                    else
                    {
                        line[_col] = b;
                    }
                    _col++;
                }
            }
        }

        public override bool CanRead 
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return true;
            }
        }

        public override long Position
        {
            get
            {
                return 0;
            }

            set
            {
            }
        }

        public override long Length
        {
            get
            {
                return 0;
            }
        }
    }
}
