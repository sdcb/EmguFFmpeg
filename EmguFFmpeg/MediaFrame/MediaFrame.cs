﻿using FFmpeg.AutoGen;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EmguFFmpeg
{
    public abstract class MediaFrame : IDisposable
    {
        protected unsafe AVFrame* pFrame;

        public MediaFrame()
        {
            unsafe
            {
                pFrame = ffmpeg.av_frame_alloc();
            }
        }

        public bool IsAudioFrame { get { unsafe { return pFrame->nb_samples > 0 && pFrame->channels > 0; } } }

        public bool IsVideoFrame { get { unsafe { return pFrame->width > 0 && pFrame->height > 0; } } }

        #region Get Managed Copy Of Data

        /// <summary>
        /// Get managed copy of <see cref="AVFrame.data"/>
        /// <para>
        /// reference <see cref="ffmpeg.av_frame_copy(AVFrame*, AVFrame*)"/>
        /// </para>
        /// </summary>
        /// <returns></returns>
        public byte[][] GetData()
        {
            unsafe
            {
                if (pFrame->width > 0 && pFrame->height > 0)
                    return GetVideoData();
                else if (pFrame->nb_samples > 0 && pFrame->channels > 0)
                    return GetAudioData();
                throw new FFmpegException(FFmpegException.InvalidFrame);
            }
        }

        /// <summary>
        /// reference <see cref="ffmpeg.av_image_copy(ref byte_ptrArray4, ref int_array4, ref byte_ptrArray4, int_array4, AVPixelFormat, int, int)"/>
        /// </summary>
        /// <returns></returns>
        private byte[][] GetVideoData()
        {
            unsafe
            {
                List<byte[]> result = new List<byte[]>();
                AVPixFmtDescriptor* desc = ffmpeg.av_pix_fmt_desc_get((AVPixelFormat)pFrame->format);
                if (desc == null || (desc->flags & ffmpeg.AV_PIX_FMT_FLAG_HWACCEL) != 0)
                    throw new FFmpegException(FFmpegException.NotSupportFrame);

                if ((desc->flags & ffmpeg.AV_PIX_FMT_FLAG_PAL) != 0 || (desc->flags & ffmpeg.AV_PIX_FMT_FLAG_PSEUDOPAL) != 0)
                {
                    result.Add(GetVideoPlane((IntPtr)pFrame->data[0], pFrame->linesize[0], pFrame->width, pFrame->height));
                    if ((desc->flags & ffmpeg.AV_PIX_FMT_FLAG_PAL) != 0 && pFrame->data[1] != null)
                    {
                        byte[] line1 = new byte[4 * 256];
                        Marshal.Copy((IntPtr)pFrame->data[1], line1, 0, line1.Length);
                        result.Add(line1);
                    }
                }
                else
                {
                    int i, planes_nb = 0;
                    for (i = 0; i < desc->nb_components; i++)
                        planes_nb = Math.Max(planes_nb, desc->comp[(uint)i].plane + 1);
                    for (i = 0; i < planes_nb; i++)
                    {
                        int h = pFrame->height;
                        int bwidth = ffmpeg.av_image_get_linesize((AVPixelFormat)pFrame->format, pFrame->width, i);
                        bwidth.ThrowExceptionIfError();
                        if (i == 1 || i == 2)
                            h = (int)Math.Ceiling((double)pFrame->height / (1 << desc->log2_chroma_h));
                        result.Add(GetVideoPlane((IntPtr)pFrame->data[(uint)i], pFrame->linesize[(uint)i], bwidth, h));
                    }
                }
                return result.ToArray();
            }
        }

        private byte[] GetVideoPlane(IntPtr srcData, int linesize, int bytewidth, int height)
        {
            if (linesize < bytewidth)
                throw new FFmpegException(FFmpegException.LineSizeError);
            byte[] result = new byte[height * linesize];
            for (int i = 0; i < height; i++)
                Marshal.Copy(srcData + i * linesize, result, i * linesize, bytewidth);
            return result;
        }

        /// <summary>
        /// reference <see cref="ffmpeg.av_samples_copy(byte**, byte**, int, int, int, int, AVSampleFormat)"/>
        /// </summary>
        /// <returns></returns>
        private byte[][] GetAudioData()
        {
            unsafe
            {
                List<byte[]> result = new List<byte[]>();
                int planar = ffmpeg.av_sample_fmt_is_planar((AVSampleFormat)pFrame->format);
                int planes = planar != 0 ? pFrame->channels : 1;
                int block_align = ffmpeg.av_get_bytes_per_sample((AVSampleFormat)pFrame->format) * (planar != 0 ? 1 : pFrame->channels);
                int data_size = pFrame->nb_samples * block_align;
                IntPtr intPtr;
                for (uint i = 0; (intPtr = (IntPtr)pFrame->extended_data[i]) != IntPtr.Zero && i < planes; i++)
                {
                    byte[] line = new byte[data_size];
                    Marshal.Copy(intPtr, line, 0, data_size);
                    result.Add(line);
                }
                return result.ToArray();
            }
        }

        #endregion

        public IntPtr[] Data
        {
            get
            {
                unsafe
                {
                    List<IntPtr> result = new List<IntPtr>();
                    IntPtr intPtr;
                    for (uint i = 0; (intPtr = (IntPtr)pFrame->extended_data[i]) != IntPtr.Zero; i++)
                        result.Add(intPtr);
                    return result.ToArray();
                }
            }
        }

        public int[] Linesize { get { unsafe { return pFrame->linesize.ToArray(); } } }

        public int Width
        {
            get { unsafe { return pFrame->width; } }
            set { unsafe { pFrame->width = value; } }
        }

        public int Height
        {
            get { unsafe { return pFrame->height; } }
            set { unsafe { pFrame->height = value; } }
        }

        public int NbSamples
        {
            get { unsafe { return pFrame->nb_samples; } }
            set { unsafe { pFrame->nb_samples = value; } }
        }

        public long Pts
        {
            get { unsafe { return pFrame->pts; } }
            set { unsafe { pFrame->pts = value; } }
        }

        public int SampleRate
        {
            get { unsafe { return pFrame->sample_rate; } }
            set { unsafe { pFrame->sample_rate = value; } }
        }

        public ulong ChannelLayout
        {
            get { unsafe { return pFrame->channel_layout; } }
            set { unsafe { pFrame->channel_layout = value; } }
        }

        public int Flags
        {
            get { unsafe { return pFrame->flags; } }
            set { unsafe { pFrame->flags = value; } }
        }

        public int Channels
        {
            get { unsafe { return pFrame->channels; } }
            set { unsafe { pFrame->channels = value; } }
        }

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            unsafe
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                    }

                    fixed (AVFrame** ppFrame = &pFrame)
                    {
                        ffmpeg.av_frame_free(ppFrame);
                    }

                    disposedValue = true;
                }
            }
        }

        ~MediaFrame()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        public void Clear()
        {
            unsafe
            {
                ffmpeg.av_frame_unref(pFrame);
            }
        }

        public abstract MediaFrame Copy();

        public AVFrame AVFrame { get { unsafe { return *pFrame; } } }

        public unsafe static implicit operator AVFrame*(MediaFrame value)
        {
            if (value == null) return null;
            return value.pFrame;
        }
    }
}