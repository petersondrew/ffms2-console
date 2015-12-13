// This is the main DLL file.

#include "stdafx.h"

#include "sdl-display.h"

namespace sdldisplay {
	Display::Display()
	{
		if (SDL_Init(SDL_INIT_VIDEO) != 0)
			throw gcnew Exception(String::Format("Error initializing SDL {0}", gcnew String(SDL_GetError())));

		window = SDL_CreateWindow("Hello World!", 100, 100, 640, 480, SDL_WINDOW_SHOWN);
		if (window == nullptr)
			throw gcnew Exception(String::Format("Error creating SDL window {0}", gcnew String(SDL_GetError())));

		renderer = SDL_CreateRenderer(window, -1, SDL_RENDERER_ACCELERATED | SDL_RENDERER_PRESENTVSYNC);
		if (renderer == nullptr)
			throw gcnew Exception(String::Format("Error creating SDL renderer {0}", gcnew String(SDL_GetError())));
	}

	Display::Display(IntPtr windowId)
	{
		if (SDL_Init(SDL_INIT_VIDEO) != 0)
			throw gcnew Exception(String::Format("Error initializing SDL {0}", gcnew String(SDL_GetError())));

		window = SDL_CreateWindowFrom(windowId.ToPointer());
		if (window == nullptr)
			throw gcnew Exception(String::Format("Error creating SDL window {0}", gcnew String(SDL_GetError())));

		WindowId = windowId;

		SDL_SetWindowTitle(window, "Grabbed by SDL!");

		renderer = SDL_CreateRenderer(window, -1, 0);
		if (renderer == nullptr)
			throw gcnew Exception(String::Format("Error creating SDL renderer {0}", gcnew String(SDL_GetError())));
	}

	Display::~Display()
	{
		this->!Display();
	}

	Display::!Display()
	{
		// TODO: Will this be called if an exception is thrown from the constructor?
		if (texture != nullptr)
			SDL_DestroyTexture(texture);
		if (renderer != nullptr)
			SDL_DestroyRenderer(renderer);
		if (window != nullptr)
			SDL_DestroyWindow(window);
		SDL_Quit();
	}

	void Display::SetSize(int w, int h)
	{
		if (window == nullptr)
			return;

		if (width == w && height == h)
			return;

		width = w;
		height = h;
		SDL_SetWindowSize(window, width, height);
		CreateTexture();
	}

	void Display::SetPixelFormat(int format)
	{
		pixelFormat = format;
		CreateTexture();
	}

	void Display::CreateTexture()
	{
		if (texture != nullptr)
			SDL_DestroyTexture(texture);

		pixelFormat = pixelFormat == 0 ? SDL_PIXELFORMAT_YV12 : pixelFormat;

		texture = SDL_CreateTexture(renderer,
			pixelFormat,
			SDL_TEXTUREACCESS_STATIC,
			width, height);
		if (texture == nullptr)
			throw gcnew Exception(String::Format("Error creating SDL texture {0}", gcnew String(SDL_GetError())));
	}

	void Display::ShowFrame(uint8_t *data[4], int lineSizes[4])
	{
		if (pixelFormat == 0)
			throw gcnew Exception(String::Format("Set the pixel format first {0}", gcnew String(SDL_GetError())));

		if (width == 0 || height == 0)
			throw gcnew Exception(String::Format("Set the output size first {0}", gcnew String(SDL_GetError())));

		if (texture == nullptr)
			CreateTexture();

		if (lineSizes[1] != 0)
		{
			if (SDL_UpdateYUVTexture(texture, NULL, data[0], lineSizes[0], data[1], lineSizes[1], data[2], lineSizes[2]) != 0)
				throw gcnew Exception(String::Format("Error updating SDL texture {0}", gcnew String(SDL_GetError())));
		}
		else
		{
			/*if (SDL_UpdateTexture(texture, NULL, data[0], lineSizes[0]) != 0)
				throw gcnew Exception(String::Format("Error updating SDL texture {0}", gcnew String(SDL_GetError())));*/

			if (SDL_UpdateYUVTexture(texture, NULL, data[0], lineSizes[0], NULL, 0, NULL, 0) != 0)
				throw gcnew Exception(String::Format("Error updating SDL texture {0}", gcnew String(SDL_GetError())));
		}

		if (SDL_RenderClear(renderer) != 0)
			throw gcnew Exception(String::Format("Error clearing SDL renderer {0}", gcnew String(SDL_GetError())));
		if (SDL_RenderCopy(renderer, texture, NULL, NULL) != 0)
			throw gcnew Exception(String::Format("Error copying SDL texture to renderer {0}", gcnew String(SDL_GetError())));
		SDL_RenderPresent(renderer);
	}
}