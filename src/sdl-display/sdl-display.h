// sdl-display.h

#pragma once
#include "SDL.h"

using namespace System;

namespace sdldisplay {

	public ref class Display
	{
	private:
		SDL_Window *window;
		SDL_Renderer *renderer;
		SDL_Texture *texture;
		IntPtr windowId;
		int width;
		int height;
		int pixelFormat;
		void CreateTexture();
	public:
		static property int PixelFormatIYUV
		{
		public:
			int get() { return SDL_PIXELFORMAT_IYUV; }
		}
		static property int PixelFormatYV12
		{
		public:
			int get() { return SDL_PIXELFORMAT_YV12; }
		}
		static property int PixelFormatYUY2
		{
		public:
			int get() { return SDL_PIXELFORMAT_YUY2; }
		}
		static property int PixelFormatRGB24
		{
		public:
			int get() { return SDL_PIXELFORMAT_RGB24; }
		}
		static property int PixelFormatBGR24
		{
		public:
			int get() { return SDL_PIXELFORMAT_BGR24; }
		}

		Display();
		Display(IntPtr windowId);
		~Display();
		!Display();
		property IntPtr WindowId
		{
		private:
			void set(IntPtr value)
			{
				windowId = value;
			}
		public:
			IntPtr get()
			{
				return windowId;
			}
		}
		void SetSize(int w, int h);
		void ShowFrame(uint8_t *data[4], int lineSizes[4]);
		void SetPixelFormat(int format);
	};
}
