"""Hugging Face Spaces entry point."""

from proper_pixel_art.web import create_demo

demo = create_demo()

if __name__ == "__main__":
    demo.launch()
